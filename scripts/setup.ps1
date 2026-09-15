[CmdletBinding()]
param(
    [string]$NuGetSource = '',
    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'EquipmentTrackingPlatform.sln'
$templateFolder = Join-Path $root 'src\EquipmentTracking.App\Templates'
$nugetConfig = Join-Path $root 'NuGet.Config'
$validationScript = Join-Path $PSScriptRoot 'validate.ps1'

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

Write-Host 'Checking project files...'
& $validationScript

Write-Host 'Checking .NET SDK...'
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw 'The .NET SDK was not found. Install the .NET 10 SDK, restart VS Code, and rerun this script.'
}

$sdkList = & dotnet --list-sdks
if ($LASTEXITCODE -ne 0) {
    throw 'The installed dotnet command could not list its SDKs.'
}

if (-not ($sdkList -match '(?m)^10\.0\.1(?:1\d|[2-9]\d)\s')) {
    throw 'The pinned .NET SDK servicing band was not found. Install SDK 10.0.110 or a later 10.0.1xx servicing patch, restart VS Code, and rerun this script.'
}

if (-not (Test-Path $nugetConfig -PathType Leaf)) {
    throw "The repository NuGet configuration is missing: $nugetConfig"
}

New-Item -ItemType Directory -Path $templateFolder -Force | Out-Null

Write-Host ''
Write-Host "Using NuGet configuration: $nugetConfig"
if ([string]::IsNullOrWhiteSpace($NuGetSource)) {
    Invoke-DotNetCommand `
        -Arguments @('nuget', 'list', 'source', '--configfile', $nugetConfig, '--format', 'Detailed') `
        -FailureMessage 'Unable to read the repository NuGet configuration'
}
else {
    Write-Host "Using package-source override: $NuGetSource"
}

# Remove stale restore/build metadata from previous failed attempts.
foreach ($projectFolder in @(
    (Join-Path $root 'src\EquipmentTracking.App'),
    (Join-Path $root 'tests\EquipmentTracking.Tests')
)) {
    foreach ($buildFolderName in @('bin', 'obj')) {
        $buildFolder = Join-Path $projectFolder $buildFolderName
        if (Test-Path $buildFolder) {
            Remove-Item $buildFolder -Recurse -Force
        }
    }
}

Write-Host ''
Write-Host 'Restoring NuGet packages...'
$restoreArguments = @(
    'restore',
    $solution,
    '--configfile',
    $nugetConfig,
    '--interactive',
    '--force',
    '--no-cache',
    '--verbosity',
    'minimal'
)

if (-not [string]::IsNullOrWhiteSpace($NuGetSource)) {
    $restoreArguments += @('--source', $NuGetSource)
}

& dotnet @restoreArguments
$restoreExitCode = $LASTEXITCODE

if ($restoreExitCode -ne 0) {
    Write-Host ''
    Write-Host 'NuGet restore did not complete.' -ForegroundColor Red
    Write-Host 'This project now uses its own NuGet.Config so user-level Package Source Mapping or disabled sources cannot silently remove nuget.org.'
    Write-Host 'If the error mentions NU1301, TLS, proxy, or an unreachable service index, your network is blocking the selected package feed.'
    Write-Host 'On a managed network, rerun setup with an approved NuGet v3 mirror:'
    Write-Host ".\scripts\setup.ps1 -NuGetSource 'https://your-approved-feed/v3/index.json'"
    throw "dotnet restore failed (exit code $restoreExitCode)."
}

Write-Host ''
Write-Host 'Auditing direct and transitive NuGet dependencies...'
& (Join-Path $PSScriptRoot 'security-scan.ps1') -NoRestore

Write-Host ''
Write-Host 'Building solution...'
Invoke-DotNetCommand `
    -Arguments @('build', $solution, '--configuration', 'Debug', '--no-restore') `
    -FailureMessage 'dotnet build failed'

if (-not $SkipTests) {
    Write-Host ''
    Write-Host 'Running tests...'
    Invoke-DotNetCommand `
        -Arguments @('test', $solution, '--configuration', 'Debug', '--no-build', '--no-restore') `
        -FailureMessage 'dotnet test failed'
}
else {
    Write-Warning 'Tests were skipped because -SkipTests was supplied.'
}

Write-Host ''
Write-Host 'Setup completed successfully.' -ForegroundColor Green
Write-Host "Place 1297-58SOW-SC-TEMPLATE.pdf in: $templateFolder"
Write-Host 'Then press F5 in VS Code.'
