[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$FailOnFinding
)

$ErrorActionPreference = 'Stop'

$sourceRoot = Join-Path $RepositoryRoot 'src\EquipmentTracking.App'
if (-not (Test-Path -LiteralPath $sourceRoot)) {
    throw "Application source folder was not found: $sourceRoot"
}

$patterns = [ordered]@{
    'HttpClient'          = '\bHttpClient\b'
    'WebClient'           = '\bWebClient\b'
    'WebRequest'          = '\b(?:HttpWebRequest|WebRequest)\b'
    'Sockets'             = '\b(?:TcpClient|UdpClient|Socket)\b'
    'GitHub URL'          = 'github\.com|raw\.githubusercontent\.com'
    'HTTP or HTTPS URL'   = 'https?://'
    'Telemetry SDK'       = '\b(?:ApplicationInsights|Sentry|OpenTelemetry)\b'
    'Automatic updater'   = '\b(?:UpdateChecker|AutoUpdater|CheckForUpdates)\b'
}

$findings = [System.Collections.Generic.List[object]]::new()
$files = Get-ChildItem -LiteralPath $sourceRoot -Recurse -File |
    Where-Object {
        $_.Extension -in '.cs', '.xaml', '.json', '.config' -and
        $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]'
    }

foreach ($file in $files) {
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $file.FullName) {
        $lineNumber++
        foreach ($entry in $patterns.GetEnumerator()) {
            if ($entry.Key -eq 'HTTP or HTTPS URL' -and
                $line -match 'schemas\.microsoft\.com/winfx|schemas\.openxmlformats\.org') {
                continue
            }

            if ($line -match $entry.Value) {
                $findings.Add([pscustomobject]@{
                    Category = $entry.Key
                    File     = $file.FullName.Substring($RepositoryRoot.Length).TrimStart('\')
                    Line     = $lineNumber
                    Text     = $line.Trim()
                })
            }
        }
    }
}

$reportFolder = Join-Path $RepositoryRoot 'artifacts\validation'
New-Item -ItemType Directory -Path $reportFolder -Force | Out-Null
$reportPath = Join-Path $reportFolder 'offline-runtime-review.json'
$reportFindings = $findings.ToArray()
ConvertTo-Json -InputObject $reportFindings -Depth 4 |
    Set-Content -LiteralPath $reportPath -Encoding UTF8

if ($findings.Count -eq 0) {
    Write-Host 'Offline runtime review passed: no obvious networking, telemetry, updater, or external URL references were found.'
    Write-Host "Report: $reportPath"
    exit 0
}

Write-Warning "Offline runtime review found $($findings.Count) item(s). Review each result; a finding is not automatically a runtime dependency."
$findings | Format-Table -AutoSize
Write-Host "Report: $reportPath"

if ($FailOnFinding) {
    exit 1
}
