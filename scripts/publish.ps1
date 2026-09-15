[CmdletBinding()]
param(
    [string]$NuGetSource = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\EquipmentTracking.App\EquipmentTracking.App.csproj'
$nugetConfig = Join-Path $root 'NuGet.Config'
$installedOutput = Join-Path $root 'artifacts\publish\win-x64'
$portableOutput = Join-Path $root 'artifacts\publish\win-x64-single-file'
$installedExecutable = Join-Path $installedOutput 'EquipmentTrackingPlatform.exe'
$portableExecutable = Join-Path $portableOutput 'EquipmentTrackingPlatform.exe'

function Invoke-DotNetCommand {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [Parameter(Mandatory)]
        [string]$FailureMessage
    )

    & dotnet @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "$FailureMessage (dotnet exit code $exitCode)."
    }
}

if (-not (Test-Path -LiteralPath $nugetConfig -PathType Leaf)) {
    throw "The repository NuGet configuration is missing: $nugetConfig"
}

foreach ($output in @($installedOutput, $portableOutput)) {
    if (Test-Path -LiteralPath $output) {
        Remove-Item -LiteralPath $output -Recurse -Force
    }
    New-Item -ItemType Directory -Path $output -Force | Out-Null
}

$restoreArguments = @(
    'restore',
    $project,
    '--runtime',
    'win-x64',
    '--configfile',
    $nugetConfig,
    '--interactive',
    '--disable-build-servers'
)
if (-not [string]::IsNullOrWhiteSpace($NuGetSource)) {
    $restoreArguments += @('--source', $NuGetSource)
}

Invoke-DotNetCommand -Arguments $restoreArguments -FailureMessage 'Publish restore failed'
& (Join-Path $PSScriptRoot 'security-scan.ps1') `
    -NoRestore `
    -Target $project `
    -ReportPrefix 'application'
& (Join-Path $PSScriptRoot 'verify-offline.ps1') -RepositoryRoot $root -FailOnFinding
$offlineReviewExitCode = $LASTEXITCODE
if ($offlineReviewExitCode -ne 0) {
    throw "Offline runtime review failed (PowerShell exit code $offlineReviewExitCode)."
}

Invoke-DotNetCommand `
    -Arguments @(
        'publish',
        $project,
        '--configuration',
        'Release',
        '--runtime',
        'win-x64',
        '--self-contained',
        'true',
        '--no-restore',
        '--disable-build-servers',
        '-p:PublishSingleFile=false',
        '-p:PublishTrimmed=false',
        '-p:PublishReadyToRun=false',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '--output',
        $installedOutput
    ) `
    -FailureMessage 'Installed-payload publish failed'

Invoke-DotNetCommand `
    -Arguments @(
        'publish',
        $project,
        '--configuration',
        'Release',
        '--runtime',
        'win-x64',
        '--self-contained',
        'true',
        '--no-restore',
        '--disable-build-servers',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:IncludeAllContentForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        '-p:PublishTrimmed=false',
        '-p:PublishReadyToRun=false',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '--output',
        $portableOutput
    ) `
    -FailureMessage 'Single-file publish failed'

if (-not (Test-Path -LiteralPath $installedExecutable -PathType Leaf)) {
    throw "The expected installed executable was not produced: $installedExecutable"
}
if (-not (Test-Path -LiteralPath $portableExecutable -PathType Leaf)) {
    throw "The expected portable executable was not produced: $portableExecutable"
}
if (@(Get-ChildItem -LiteralPath $installedOutput -File -Recurse).Count -lt 2) {
    throw 'The installed publish unexpectedly contains fewer than two payload files.'
}

$unexpectedFiles = Get-ChildItem -LiteralPath $portableOutput -File -Recurse |
    Where-Object { $_.FullName -ne $portableExecutable }
if (@($unexpectedFiles).Count -gt 0) {
    $names = @($unexpectedFiles | ForEach-Object { $_.Name }) -join ', '
    throw "Single-file publishing produced unexpected external runtime/content files: $names"
}

foreach ($output in @($installedOutput, $portableOutput)) {
    $sumLines = Get-ChildItem -LiteralPath $output -File -Recurse |
        Sort-Object FullName |
        ForEach-Object {
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            $relativePath = $_.FullName.Substring($output.Length).TrimStart(
                [System.IO.Path]::DirectorySeparatorChar)
            "$hash  $relativePath"
        }
    $manifestPath = Join-Path $output 'SHA256SUMS.txt'
    $sumLines | Set-Content -LiteralPath $manifestPath -Encoding UTF8
}

Write-Host "Published conventional MSI payload to $installedOutput"
Write-Host "Published self-contained portable executable to $portableExecutable"
