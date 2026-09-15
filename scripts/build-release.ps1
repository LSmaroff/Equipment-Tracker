[CmdletBinding()]
param(
    [string]$NuGetSource = '',
    [switch]$SkipTests,
    [string]$CertificateThumbprint = '',
    [string]$PfxPath = '',
    [securestring]$PfxPassword,
    [string]$TimestampUrl = '',
    [string]$SignToolPath = 'signtool.exe',
    [switch]$AllowUnsignedPilotBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\EquipmentTracking.App\EquipmentTracking.App.csproj'
$installerProject = Join-Path $root 'installer\EquipmentTracking.Installer\EquipmentTracking.Installer.wixproj'
$nugetConfig = Join-Path $root 'NuGet.Config'
$installedPublishDirectory = Join-Path $root 'artifacts\publish\win-x64'
$portablePublishDirectory = Join-Path $root 'artifacts\publish\win-x64-single-file'
$installerDirectory = Join-Path $root 'artifacts\installer'
$releaseDirectory = Join-Path $root 'artifacts\release'

function Invoke-DotNetCommand {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [Parameter(Mandatory)]
        [string]$FailureMessage
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (dotnet exit code $LASTEXITCODE)."
    }
}

function Remove-GeneratedBuildDirectory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    try {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    catch {
        $removalError = $_
        try {
            $remainingEntries = @(Get-ChildItem -LiteralPath $Path -Force -ErrorAction Stop)
        }
        catch {
            throw (
                "Could not verify the generated build directory '$Path' after cleanup failed. " +
                $removalError.Exception.Message)
        }

        if ($remainingEntries.Count -eq 0) {
            Write-Warning (
                "The empty generated build directory '$Path' is held open by another process. " +
                'Its contents were removed successfully, so the release cleanup remains complete.')
            return
        }

        throw (
            "Could not remove the generated build directory '$Path'. " +
            'Close Equipment Tracking Platform, Visual Studio, and any active dotnet build process, then retry. ' +
            $removalError.Exception.Message)
    }
}

[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$versionNode = $projectXml.SelectSingleNode('/Project/PropertyGroup/Version')
if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    throw 'The application project does not define a Version value.'
}
$applicationVersion = [string]$versionNode.InnerText
$msiVersion = ($applicationVersion -split '-', 2)[0]
if ($msiVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "The MSI version must contain three numeric fields. Actual: $msiVersion"
}
$msiVersionParts = @($msiVersion.Split('.') | ForEach-Object { [uint64]$_ })
if ($msiVersionParts[0] -gt 255 -or
    $msiVersionParts[1] -gt 255 -or
    $msiVersionParts[2] -gt 65535) {
    throw "The MSI version exceeds Windows Installer limits (major/minor 255, build 65535): $msiVersion"
}

$signingRequested =
    -not [string]::IsNullOrWhiteSpace($CertificateThumbprint) -or
    -not [string]::IsNullOrWhiteSpace($PfxPath)
if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint) -and
    -not [string]::IsNullOrWhiteSpace($PfxPath)) {
    throw 'Provide CertificateThumbprint or PfxPath, not both.'
}
if (-not $signingRequested -and -not $AllowUnsignedPilotBuild) {
    throw 'Release output must be signed. Supply CertificateThumbprint or PfxPath. Use -AllowUnsignedPilotBuild only for an explicitly approved synthetic-data pilot; its file names will be marked UNSIGNED-PILOT.'
}
if ($signingRequested -and $AllowUnsignedPilotBuild) {
    throw 'AllowUnsignedPilotBuild cannot be combined with a signing identity. Remove the pilot flag for a signed field candidate.'
}
if ($SkipTests -and -not $AllowUnsignedPilotBuild) {
    throw 'A signed field candidate cannot skip tests. SkipTests is restricted to an explicitly marked unsigned synthetic-data pilot.'
}

# The WPF build caches generated .g.cs/BAML inputs in persistent MSBuild and
# compiler servers. Quiesce those servers before deleting obj so a later test
# cannot reuse in-memory state that points at generated files just removed.
Write-Host 'Stopping persistent .NET build servers...'
Invoke-DotNetCommand `
    -Arguments @('build-server', 'shutdown') `
    -FailureMessage 'Persistent .NET build servers could not be stopped'

# WPF stores generated code, BAML, and incremental markup-compiler state below
# obj. A source update laid over an older working folder can otherwise leave an
# obsolete cache that references a BAML file which no longer exists (BG1002).
# A field release must always begin from fresh, known generated state.
$generatedBuildDirectories = @(
    (Join-Path $root 'src\EquipmentTracking.App\bin'),
    (Join-Path $root 'src\EquipmentTracking.App\obj'),
    (Join-Path $root 'tests\EquipmentTracking.Tests\bin'),
    (Join-Path $root 'tests\EquipmentTracking.Tests\obj'),
    (Join-Path $root 'installer\EquipmentTracking.Installer\bin'),
    (Join-Path $root 'installer\EquipmentTracking.Installer\obj')
)

Write-Host 'Removing generated build state from previous versions...'
foreach ($generatedBuildDirectory in $generatedBuildDirectories) {
    Remove-GeneratedBuildDirectory -Path $generatedBuildDirectory
}

if (-not $SkipTests) {
    $applicationRestoreArguments = @(
        'restore',
        (Join-Path $root 'EquipmentTrackingPlatform.sln'),
        '--configfile',
        $nugetConfig,
        '--interactive',
        '--disable-build-servers'
    )
    if (-not [string]::IsNullOrWhiteSpace($NuGetSource)) {
        $applicationRestoreArguments += @('--source', $NuGetSource)
    }
    Invoke-DotNetCommand `
        -Arguments $applicationRestoreArguments `
        -FailureMessage 'Release restore failed'
    Invoke-DotNetCommand `
        -Arguments @(
            'test',
            (Join-Path $root 'EquipmentTrackingPlatform.sln'),
            '--no-restore',
            '--disable-build-servers'
        ) `
        -FailureMessage 'Release tests failed'
}

& (Join-Path $PSScriptRoot 'validate.ps1')
& (Join-Path $PSScriptRoot 'publish.ps1') -NuGetSource $NuGetSource

if ($signingRequested) {
    $signArguments = @{
        SignToolPath = $SignToolPath
    }
    if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
        $signArguments.TimestampUrl = $TimestampUrl
    }
    if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        $signArguments.CertificateThumbprint = $CertificateThumbprint
    }
    else {
        $signArguments.PfxPath = $PfxPath
        if ($null -ne $PfxPassword) {
            $signArguments.PfxPassword = $PfxPassword
        }
    }

    $signScript = Join-Path $PSScriptRoot 'sign-release.ps1'
    $signArguments['PublishDirectory'] = $installedPublishDirectory
    & $signScript @signArguments
    $signArguments['PublishDirectory'] = $portablePublishDirectory
    & $signScript @signArguments
}

$restoreArguments = @(
    'restore',
    $installerProject,
    '--configfile',
    $nugetConfig,
    '--interactive',
    '--disable-build-servers'
)
if (-not [string]::IsNullOrWhiteSpace($NuGetSource)) {
    $restoreArguments += @('--source', $NuGetSource)
}
Invoke-DotNetCommand -Arguments $restoreArguments -FailureMessage 'Installer restore failed'
& (Join-Path $PSScriptRoot 'security-scan.ps1') `
    -NoRestore `
    -Target $installerProject `
    -ReportPrefix 'installer'

if (Test-Path -LiteralPath $installerDirectory) {
    Remove-Item -LiteralPath $installerDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $installerDirectory -Force | Out-Null

Invoke-DotNetCommand `
    -Arguments @(
        'build',
        $installerProject,
        '--configuration',
        'Release',
        '--no-restore',
        '--disable-build-servers',
        "-p:PublishDirectory=$installedPublishDirectory",
        "-p:ProductVersion=$msiVersion"
    ) `
    -FailureMessage 'MSI build failed'

$executable = Join-Path $portablePublishDirectory 'EquipmentTrackingPlatform.exe'
$msi = Get-ChildItem -LiteralPath $installerDirectory -Filter '*.msi' -File -Recurse |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "The release executable is missing: $executable"
}
if ($null -eq $msi) {
    throw "The MSI was not produced below $installerDirectory"
}

if ($signingRequested) {
    $signArguments['AdditionalOnly'] = $true
    $signArguments['AdditionalPath'] = @($msi.FullName)
    & $signScript @signArguments
}

if (Test-Path -LiteralPath $releaseDirectory) {
    Remove-Item -LiteralPath $releaseDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
$releaseQualifier = if ($signingRequested) { '' } else { '-UNSIGNED-PILOT' }
$releaseUse = if ($signingRequested) {
    'Field candidate'
}
else {
    'UNSIGNED SYNTHETIC-DATA PILOT ONLY'
}
$releaseExe = Join-Path $releaseDirectory "EquipmentTrackingPlatform-$applicationVersion-win-x64$releaseQualifier.exe"
$releaseMsi = Join-Path $releaseDirectory "EquipmentTrackingPlatform-$applicationVersion-win-x64$releaseQualifier.msi"
Copy-Item -LiteralPath $executable -Destination $releaseExe
Copy-Item -LiteralPath $msi.FullName -Destination $releaseMsi

$releaseManifest = [ordered]@{
    Product = 'Equipment Tracking Platform'
    ApplicationVersion = $applicationVersion
    MsiProductVersion = $msiVersion
    DatabaseSchemaVersion = 6
    Runtime = 'win-x64'
    SelfContained = $true
    OfflineRuntime = $true
    AuthenticodeSigned = [bool]$signingRequested
    TestsSkipped = [bool]$SkipTests
    ReleaseUse = $releaseUse
    PortableSingleFile = $true
    MsiPayload = 'Self-contained conventional multi-file'
    MsiScope = 'Per-machine (authorized administrator or software distribution required)'
    CreatedAt = [DateTimeOffset]::Now.ToString('O')
    Files = @(
        [ordered]@{
            Name = [System.IO.Path]::GetFileName($releaseExe)
            Sha256 = (Get-FileHash -LiteralPath $releaseExe -Algorithm SHA256).Hash.ToLowerInvariant()
            SizeBytes = (Get-Item -LiteralPath $releaseExe).Length
        },
        [ordered]@{
            Name = [System.IO.Path]::GetFileName($releaseMsi)
            Sha256 = (Get-FileHash -LiteralPath $releaseMsi -Algorithm SHA256).Hash.ToLowerInvariant()
            SizeBytes = (Get-Item -LiteralPath $releaseMsi).Length
        }
    )
}
$manifestPath = Join-Path $releaseDirectory 'release-manifest.json'
$releaseManifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$sumLines = Get-ChildItem -LiteralPath $releaseDirectory -File |
    Where-Object { $_.Name -ne 'SHA256SUMS.txt' } |
    Sort-Object Name |
    ForEach-Object {
        $fileHash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$fileHash  $($_.Name)"
    }
$sumPath = Join-Path $releaseDirectory 'SHA256SUMS.txt'
$sumLines | Set-Content -LiteralPath $sumPath -Encoding UTF8

Write-Host "Release artifacts: $releaseDirectory"
Write-Host "Portable EXE: $releaseExe"
Write-Host "Machine-wide MSI: $releaseMsi"
if (-not $signingRequested) {
    Write-Warning 'UNSIGNED-PILOT output was created for explicitly approved synthetic-data testing only. Rebuild with the organization-approved signing identity before field deployment.'
}
