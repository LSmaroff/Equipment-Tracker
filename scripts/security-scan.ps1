[CmdletBinding()]
param(
    [switch]$NoRestore,
    [string]$Target = '',
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$')]
    [string]$ReportPrefix = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'EquipmentTrackingPlatform.sln'
if ([string]::IsNullOrWhiteSpace($Target)) {
    $Target = $solution
}
elseif (-not [System.IO.Path]::IsPathRooted($Target)) {
    $Target = Join-Path $root $Target
}

if (-not (Test-Path $Target -PathType Leaf)) {
    throw "The vulnerability-audit target was not found: $Target"
}

$reportDirectory = Join-Path $root 'artifacts\security'
$filePrefix = if ([string]::IsNullOrWhiteSpace($ReportPrefix)) {
    ''
}
else {
    $ReportPrefix + '-'
}
$auditJsonPath = Join-Path $reportDirectory ($filePrefix + 'package-audit.json')
$inventoryJsonPath = Join-Path $reportDirectory ($filePrefix + 'dependency-inventory.json')

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK was not found.'
}

function Invoke-DotNetCapture {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [Parameter(Mandatory)]
        [string]$OutputPath,

        [Parameter(Mandatory)]
        [string]$FailureMessage
    )

    $captured = (& dotnet @Arguments 2>&1 | Out-String)
    $exitCode = $LASTEXITCODE
    $captured | Set-Content -Path $OutputPath -Encoding utf8

    if ($exitCode -ne 0) {
        throw "$FailureMessage (dotnet exit code $exitCode). See $OutputPath"
    }

    return $captured
}

function Test-ContainsVulnerability {
    param([AllowNull()]$Node)

    if ($null -eq $Node -or $Node -is [string] -or $Node.GetType().IsPrimitive) {
        return $false
    }

    $vulnerabilityProperty = $Node.PSObject.Properties['vulnerabilities']
    if ($null -ne $vulnerabilityProperty -and
        $null -ne $vulnerabilityProperty.Value -and
        @($vulnerabilityProperty.Value).Count -gt 0) {
        return $true
    }

    if ($Node -is [System.Collections.IEnumerable]) {
        foreach ($item in $Node) {
            if (Test-ContainsVulnerability -Node $item) {
                return $true
            }
        }

        return $false
    }

    foreach ($property in $Node.PSObject.Properties) {
        if (Test-ContainsVulnerability -Node $property.Value) {
            return $true
        }
    }

    return $false
}

New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null

$commonArguments = @('list', $Target, 'package', '--include-transitive', '--format', 'json', '--output-version', '1')
if ($NoRestore) {
    $commonArguments += '--no-restore'
}

$inventoryOutput = Invoke-DotNetCapture `
    -Arguments $commonArguments `
    -OutputPath $inventoryJsonPath `
    -FailureMessage 'NuGet dependency inventory failed'

# Validate that the inventory output is valid JSON. The parsed result is not otherwise
# used because the raw file is retained as build evidence.
$null = $inventoryOutput | ConvertFrom-Json

$auditArguments = @('list', $Target, 'package', '--vulnerable', '--include-transitive', '--format', 'json', '--output-version', '1')
if ($NoRestore) {
    $auditArguments += '--no-restore'
}

$auditOutput = Invoke-DotNetCapture `
    -Arguments $auditArguments `
    -OutputPath $auditJsonPath `
    -FailureMessage 'NuGet vulnerability audit failed'
$auditData = $auditOutput | ConvertFrom-Json

if (Test-ContainsVulnerability -Node $auditData) {
    throw "Known vulnerable NuGet dependencies were found. See $auditJsonPath"
}

Write-Host "NuGet dependency audit passed: $auditJsonPath" -ForegroundColor Green
Write-Host "Dependency inventory written: $inventoryJsonPath" -ForegroundColor Green
