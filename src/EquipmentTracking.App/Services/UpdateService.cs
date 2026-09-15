using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EquipmentTracking.App.Models;

namespace EquipmentTracking.App.Services;

public sealed class UpdateService
{
    public const string ExpectedProductName = "Equipment Tracking Platform";
    public const string ExpectedUpgradeCode = "{C6534286-9C99-45F3-A3AF-F70508F03208}";

    public async Task<UpdatePackageInspection> InspectAsync(
        string msiPath,
        Version? currentVersion = null,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(msiPath ?? string.Empty);
        if (!File.Exists(fullPath) ||
            !string.Equals(Path.GetExtension(fullPath), ".msi", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("Select an Equipment Tracking Platform MSI package.", fullPath);
        }

        var properties = ReadMsiProperties(fullPath, "ProductName", "ProductVersion", "UpgradeCode");
        var productName = properties["ProductName"];
        var upgradeCode = properties["UpgradeCode"];
        if (!string.Equals(productName, ExpectedProductName, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The selected MSI is for '{productName}', not {ExpectedProductName}.");
        }

        if (!string.Equals(upgradeCode, ExpectedUpgradeCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The selected MSI does not use the approved Equipment Tracking Platform upgrade identity.");
        }

        if (!Version.TryParse(properties["ProductVersion"], out var targetVersion))
        {
            throw new InvalidDataException("The selected MSI has an invalid ProductVersion.");
        }

        var installedVersion = currentVersion ?? GetCurrentProductVersion();
        if (targetVersion <= installedVersion)
        {
            throw new InvalidOperationException(
                $"The selected MSI is version {targetVersion}, but the running application is {installedVersion}. Select a newer package.");
        }

        var fileInfo = new FileInfo(fullPath);
        var sha256 = await ComputeSha256Async(fullPath, cancellationToken);
        var signatureTrusted = AuthenticodeVerifier.IsTrusted(fullPath);
        var manifestPath = Path.Combine(fileInfo.DirectoryName!, "release-manifest.json");
        var manifestFound = File.Exists(manifestPath);
        var manifestVerified = false;
        var authenticodeSigned = signatureTrusted;
        var releaseUse = string.Empty;
        var warning = string.Empty;

        if (manifestFound)
        {
            await using var stream = File.OpenRead(manifestPath);
            using var manifest = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = manifest.RootElement;
            var manifestProduct = root.GetProperty("Product").GetString() ?? string.Empty;
            var manifestMsiVersion = root.GetProperty("MsiProductVersion").GetString() ?? string.Empty;
            if (!string.Equals(manifestProduct, ExpectedProductName, StringComparison.Ordinal) ||
                !string.Equals(manifestMsiVersion, targetVersion.ToString(3), StringComparison.Ordinal))
            {
                throw new InvalidDataException("The companion release manifest does not match the selected MSI product or version.");
            }

            var fileEntry = root.GetProperty("Files")
                .EnumerateArray()
                .FirstOrDefault(item => string.Equals(
                    item.GetProperty("Name").GetString(),
                    fileInfo.Name,
                    StringComparison.OrdinalIgnoreCase));
            if (fileEntry.ValueKind == JsonValueKind.Undefined)
            {
                throw new InvalidDataException("The selected MSI is not listed in the companion release manifest.");
            }

            var expectedHash = fileEntry.GetProperty("Sha256").GetString() ?? string.Empty;
            var expectedSize = fileEntry.GetProperty("SizeBytes").GetInt64();
            if (!string.Equals(expectedHash, sha256, StringComparison.OrdinalIgnoreCase) ||
                expectedSize != fileInfo.Length)
            {
                throw new InvalidDataException("The selected MSI failed its companion manifest SHA-256 or size check.");
            }

            var manifestClaimsSignature =
                root.TryGetProperty("AuthenticodeSigned", out var signedElement) &&
                signedElement.ValueKind == JsonValueKind.True;
            if (manifestClaimsSignature && !signatureTrusted)
            {
                throw new InvalidDataException(
                    "The release manifest marks the MSI as signed, but Windows could not validate its Authenticode signature offline.");
            }
            releaseUse = root.TryGetProperty("ReleaseUse", out var useElement)
                ? useElement.GetString() ?? string.Empty
                : string.Empty;
            manifestVerified = true;
            if (!signatureTrusted)
            {
                warning = "The manifest marks this as an unsigned pilot package. Do not use it for field data.";
            }
        }
        else
        {
            warning = signatureTrusted
                ? "The MSI Authenticode signature is valid, but no companion release-manifest.json was found for the release hash and size check."
                : "No companion release-manifest.json was found and Windows could not validate an Authenticode signature. The MSI identity was checked and its SHA-256 is shown, but package provenance was not verified.";
        }

        return new UpdatePackageInspection
        {
            MsiPath = fullPath,
            ProductName = productName,
            ProductVersion = targetVersion,
            UpgradeCode = upgradeCode,
            Sha256 = sha256,
            SizeBytes = fileInfo.Length,
            ManifestFound = manifestFound,
            ManifestVerified = manifestVerified,
            AuthenticodeSigned = authenticodeSigned,
            ReleaseUse = releaseUse,
            Warning = warning
        };
    }

    public void LaunchInstaller(UpdatePackageInspection package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var startInfo = new ProcessStartInfo
        {
            FileName = "msiexec.exe",
            UseShellExecute = true,
            Verb = "runas"
        };
        startInfo.ArgumentList.Add("/i");
        startInfo.ArgumentList.Add(package.MsiPath);
        startInfo.ArgumentList.Add("/passive");
        startInfo.ArgumentList.Add("/norestart");
        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("Windows Installer could not be started.");
        }
    }

    private static Version GetCurrentProductVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version is null
            ? new Version(0, 0, 0)
            : new Version(version.Major, version.Minor, Math.Max(0, version.Build));
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 128,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static Dictionary<string, string> ReadMsiProperties(
        string path,
        params string[] propertyNames)
    {
        EnsureWindowsInstallerResult(MsiOpenDatabase(path, IntPtr.Zero, out var database), "open MSI database");
        try
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var propertyName in propertyNames)
            {
                var query = $"SELECT `Value` FROM `Property` WHERE `Property`='{propertyName}'";
                EnsureWindowsInstallerResult(MsiDatabaseOpenView(database, query, out var view), "read MSI properties");
                try
                {
                    EnsureWindowsInstallerResult(MsiViewExecute(view, IntPtr.Zero), "execute MSI property query");
                    EnsureWindowsInstallerResult(MsiViewFetch(view, out var record), $"find MSI property {propertyName}");
                    try
                    {
                        var capacity = 512u;
                        var builder = new StringBuilder((int)capacity);
                        EnsureWindowsInstallerResult(
                            MsiRecordGetString(record, 1, builder, ref capacity),
                            $"read MSI property {propertyName}");
                        result[propertyName] = builder.ToString();
                    }
                    finally
                    {
                        MsiCloseHandle(record);
                    }
                }
                finally
                {
                    MsiCloseHandle(view);
                }
            }

            return result;
        }
        finally
        {
            MsiCloseHandle(database);
        }
    }

    private static void EnsureWindowsInstallerResult(uint result, string operation)
    {
        if (result != 0)
        {
            throw new InvalidDataException(
                $"Windows Installer could not {operation} (error {result.ToString(CultureInfo.InvariantCulture)})." );
        }
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiOpenDatabase(string databasePath, IntPtr persist, out IntPtr database);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiDatabaseOpenView(IntPtr database, string query, out IntPtr view);

    [DllImport("msi.dll")]
    private static extern uint MsiViewExecute(IntPtr view, IntPtr record);

    [DllImport("msi.dll")]
    private static extern uint MsiViewFetch(IntPtr view, out IntPtr record);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiRecordGetString(
        IntPtr record,
        uint field,
        StringBuilder value,
        ref uint valueLength);

    [DllImport("msi.dll")]
    private static extern uint MsiCloseHandle(IntPtr handle);
}
