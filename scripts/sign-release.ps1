[CmdletBinding()]
param(
    [string]$PublishDirectory = '',

    [string]$CertificateThumbprint = '',

    [string]$PfxPath = '',

    [securestring]$PfxPassword,

    [string]$TimestampUrl,
    [string]$SignToolPath = 'signtool.exe',
    [string[]]$AdditionalPath = @(),
    [switch]$AdditionalOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$hasCertificateThumbprint = -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)
$hasPfxPath = -not [string]::IsNullOrWhiteSpace($PfxPath)
if ($hasCertificateThumbprint -eq $hasPfxPath) {
    throw 'Provide exactly one signing identity: CertificateThumbprint or PfxPath.'
}

function Resolve-SignToolExecutable {
    [CmdletBinding()]
    param(
        [AllowEmptyString()]
        [string]$RequestedPath
    )

    $requested = if ([string]::IsNullOrWhiteSpace($RequestedPath)) {
        'signtool.exe'
    }
    else {
        $RequestedPath.Trim()
    }

    $command = Get-Command -Name $requested -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -ne $command) {
        return [string]$command.Source
    }

    $isDefaultName = [string]::Equals(
        $requested,
        'signtool.exe',
        [StringComparison]::OrdinalIgnoreCase)
    if (-not $isDefaultName) {
        throw "SignTool was not found at the supplied -SignToolPath value: '$requested'."
    }

    $windowsSdkVerBinPath = [Environment]::GetEnvironmentVariable('WindowsSdkVerBinPath')
    $windowsSdkBinPath = [Environment]::GetEnvironmentVariable('WindowsSdkBinPath')
    $windowsSdkDirectory = [Environment]::GetEnvironmentVariable('WindowsSdkDir')
    $programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    $programFiles = [Environment]::GetEnvironmentVariable('ProgramFiles')
    $processorArchitecture = [Environment]::GetEnvironmentVariable('PROCESSOR_ARCHITECTURE')

    $sdkBinRoots = @()
    foreach ($environmentPath in @(
        $windowsSdkVerBinPath,
        $windowsSdkBinPath
    )) {
        if (-not [string]::IsNullOrWhiteSpace($environmentPath)) {
            $sdkBinRoots += $environmentPath
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($windowsSdkDirectory)) {
        $sdkBinRoots += (Join-Path $windowsSdkDirectory 'bin')
    }

    foreach ($registryPath in @(
        'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots',
        'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots'
    )) {
        try {
            $installedRoots = Get-ItemProperty -LiteralPath $registryPath -ErrorAction Stop
            $kitsRootProperty = $installedRoots.PSObject.Properties['KitsRoot10']
            if ($null -ne $kitsRootProperty -and
                -not [string]::IsNullOrWhiteSpace([string]$kitsRootProperty.Value)) {
                $sdkBinRoots += (Join-Path ([string]$kitsRootProperty.Value) 'bin')
            }
        }
        catch {
            # The SDK can be installed without both registry views being present.
        }
    }

    foreach ($programFilesRoot in @(
        $programFilesX86,
        $programFiles
    )) {
        if (-not [string]::IsNullOrWhiteSpace($programFilesRoot)) {
            $sdkBinRoots += (Join-Path $programFilesRoot 'Windows Kits\10\bin')
        }
    }

    $sdkBinRoots = @(
        $sdkBinRoots |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Select-Object -Unique
    )

    $architectures = @('x64', 'x86', 'arm64')
    if ([string]::Equals(
        [string]$processorArchitecture,
        'ARM64',
        [StringComparison]::OrdinalIgnoreCase)) {
        $architectures = @('arm64', 'x64', 'x86')
    }

    $candidatePaths = @()
    foreach ($sdkBinRoot in $sdkBinRoots) {
        foreach ($architecture in $architectures) {
            $candidatePaths += (Join-Path $sdkBinRoot "$architecture\signtool.exe")
        }

        $versionDirectories = @(
            Get-ChildItem -LiteralPath $sdkBinRoot -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
                Sort-Object -Property @{
                    Expression = { [version]$_.Name }
                    Descending = $true
                }
        )
        foreach ($architecture in $architectures) {
            foreach ($versionDirectory in $versionDirectories) {
                $candidatePaths += (Join-Path $versionDirectory.FullName "$architecture\signtool.exe")
            }
        }
    }

    foreach ($candidatePath in @($candidatePaths | Select-Object -Unique)) {
        if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
            $resolvedCandidate = Get-Item -LiteralPath $candidatePath
            return [string]$resolvedCandidate.FullName
        }
    }

    $checkedRoots = if ($sdkBinRoots.Count -gt 0) {
        $sdkBinRoots -join '; '
    }
    else {
        '(no Windows SDK Bin roots were detected)'
    }
    $notFoundMessage = @(
        'signtool.exe was not found in PATH or the installed Windows SDK folders.'
        'The SDK may be installed without its signing-tools component.'
        'Modify the Windows SDK installation to include Signing Tools for Desktop Apps,'
        'or provide -SignToolPath with the full x64 signtool.exe path.'
        "Checked SDK Bin roots: $checkedRoots"
    ) -join ' '
    throw $notFoundMessage
}

$signToolExecutable = Resolve-SignToolExecutable -RequestedPath $SignToolPath
Write-Host "Using SignTool: $signToolExecutable"

$targets = @()
if (-not $AdditionalOnly) {
    if (-not (Test-Path -LiteralPath $PublishDirectory -PathType Container)) {
        throw "Publish directory was not found: $PublishDirectory"
    }
    $targets += @(
        (Join-Path $PublishDirectory 'EquipmentTrackingPlatform.exe'),
        (Join-Path $PublishDirectory 'EquipmentTrackingPlatform.dll'),
        (Join-Path $PublishDirectory 'EquipmentTracking.App.exe'),
        (Join-Path $PublishDirectory 'EquipmentTracking.App.dll')
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
}
foreach ($path in $AdditionalPath) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Additional signing target was not found: $path"
    }
    $targets += (Resolve-Path -LiteralPath $path).Path
}
$targets = @($targets | Select-Object -Unique)

if ($targets.Count -eq 0) {
    throw 'No application-owned EXE or DLL was found in the publish directory.'
}

$baseArguments = @('sign', '/fd', 'SHA256')
if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
    $baseArguments += @('/tr', $TimestampUrl, '/td', 'SHA256')
}

if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $baseArguments += @('/sha1', ($CertificateThumbprint -replace '\s', ''))
}
else {
    if (-not (Test-Path -LiteralPath $PfxPath -PathType Leaf)) {
        throw "PFX file was not found: $PfxPath"
    }

    $baseArguments += @('/f', $PfxPath)
    if ($null -ne $PfxPassword) {
        $plainPassword = [System.Net.NetworkCredential]::new('', $PfxPassword).Password
        $baseArguments += @('/p', $plainPassword)
    }
}

try {
    foreach ($target in $targets) {
        Write-Host "Signing $target"
        & $signToolExecutable @baseArguments $target
        if ($LASTEXITCODE -ne 0) {
            throw "SignTool failed while signing $target. Exit code: $LASTEXITCODE"
        }

        & $signToolExecutable verify /pa /all /v $target
        if ($LASTEXITCODE -ne 0) {
            throw "Signature verification failed for $target. Exit code: $LASTEXITCODE"
        }
    }
}
finally {
    if (Get-Variable plainPassword -ErrorAction SilentlyContinue) {
        $plainPassword = $null
    }
}

Write-Host 'Application-owned release files were signed and verified.'
Write-Warning 'An organization-approved code-signing certificate and any required internal timestamp service must be supplied outside the source code. Do not commit PFX files or passwords.'
