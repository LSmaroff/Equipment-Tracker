[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string]$NuGetSource = '',

    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'EquipmentTrackingPlatform.sln'
$nugetConfig = Join-Path $root 'NuGet.Config'

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

if (-not (Test-Path $nugetConfig -PathType Leaf)) {
    throw "The repository NuGet configuration is missing: $nugetConfig"
}

$restoreArguments = @(
    'restore',
    $solution,
    '--configfile',
    $nugetConfig,
    '--interactive'
)

if (-not [string]::IsNullOrWhiteSpace($NuGetSource)) {
    $restoreArguments += @('--source', $NuGetSource)
}

Invoke-DotNetCommand -Arguments $restoreArguments -FailureMessage 'Restore failed'
& (Join-Path $PSScriptRoot 'security-scan.ps1') -NoRestore -Target $solution
Invoke-DotNetCommand `
    -Arguments @('build', $solution, '--configuration', $Configuration, '--no-restore') `
    -FailureMessage 'Build failed'

if (-not $SkipTests) {
    Invoke-DotNetCommand `
        -Arguments @('test', $solution, '--configuration', $Configuration, '--no-build', '--no-restore') `
        -FailureMessage 'Tests failed'
}
