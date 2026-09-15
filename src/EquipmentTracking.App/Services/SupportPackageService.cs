using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;

namespace EquipmentTracking.App.Services;

public sealed class SupportPackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;
    private readonly SettingsService _settings;
    private readonly DatabaseService _database;
    private readonly PdfFormService _pdfForms;
    private readonly CacCertificateService _cacCertificates;
    private readonly WorkflowJournalService _journals;
    private readonly PreflightService _preflight;
    private readonly FileLogger _logger;

    public SupportPackageService(
        AppPaths paths,
        SettingsService settings,
        DatabaseService database,
        PdfFormService pdfForms,
        CacCertificateService cacCertificates,
        WorkflowJournalService journals,
        PreflightService preflight,
        FileLogger logger)
    {
        _paths = paths;
        _settings = settings;
        _database = database;
        _pdfForms = pdfForms;
        _cacCertificates = cacCertificates;
        _journals = journals;
        _preflight = preflight;
        _logger = logger;
    }

    public async Task<string> CreateAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureDirectories();
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var staging = Path.Combine(_paths.DiagnosticsDirectory, $"support-{timestamp}-{Guid.NewGuid():N}");
        var destination = Path.Combine(
            _paths.DiagnosticsDirectory,
            $"EquipmentTracking-Support-{timestamp}-{Guid.NewGuid():N}.zip");
        Directory.CreateDirectory(staging);

        try
        {
            var applicationInfo = new
            {
                Application = "Equipment Tracking Platform",
                Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown",
                Build = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
                OperatingSystem = Environment.OSVersion.VersionString,
                Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                ComputerIdentifier = BuildNonReversibleIdentifier(Environment.MachineName),
                UserIdentifier = BuildNonReversibleIdentifier(Environment.UserName),
                ProcessId = Environment.ProcessId,
                CreatedAt = DateTimeOffset.Now
            };
            await WriteJsonAsync(Path.Combine(staging, "application-info.json"), applicationInfo, cancellationToken);

            var sanitizedSettings = _settings.Current.Clone();
            sanitizedSettings.TemplatePdfPath = SanitizePath(sanitizedSettings.TemplatePdfPath);
            sanitizedSettings.CompletedPdfFolder = SanitizePath(sanitizedSettings.CompletedPdfFolder);
            sanitizedSettings.ExcelExportPath = SanitizePath(sanitizedSettings.ExcelExportPath);
            sanitizedSettings.AdobeExecutablePath = SanitizePath(sanitizedSettings.AdobeExecutablePath);
            sanitizedSettings.ScheduledBackupFolder = SanitizePath(sanitizedSettings.ScheduledBackupFolder);
            await WriteJsonAsync(Path.Combine(staging, "settings-sanitized.json"), sanitizedSettings, cancellationToken);

            var schemaVersion = await _database.GetCurrentSchemaVersionAsync(cancellationToken);
            var integrity = await _database.VerifyIntegrityAsync(cancellationToken);
            await WriteJsonAsync(
                Path.Combine(staging, "database-summary.json"),
                new { SchemaVersion = schemaVersion, Integrity = integrity },
                cancellationToken);

            var templatePath = _settings.ResolvePath(_settings.Current.TemplatePdfPath);
            if (File.Exists(templatePath))
            {
                var inspection = _pdfForms.InspectTemplate(
                    templatePath,
                    _settings.Current.PdfFieldMappings.Values.ToArray());
                await WriteJsonAsync(
                    Path.Combine(staging, "template-inspection.json"),
                    new
                    {
                        PdfPath = SanitizePath(inspection.PdfPath),
                        inspection.FieldNames,
                        inspection.SignatureFieldCount,
                        inspection.NeedAppearances,
                        inspection.ContainsJavaScriptMarkers,
                        inspection.MissingRequiredFields,
                        inspection.IsReady
                    },
                    cancellationToken);
            }

            try
            {
                var readers = _cacCertificates.GetReaderNamesForDiagnostics();
                var inserted = _cacCertificates.GetReaderNamesForDiagnostics(insertedCardsOnly: true);
                await WriteJsonAsync(
                    Path.Combine(staging, "smart-card-readers.json"),
                    new { Readers = readers, InsertedCardReaders = inserted },
                    cancellationToken);
            }
            catch (Exception ex)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(staging, "smart-card-readers.txt"),
                    SanitizeText(ex.ToString()),
                    cancellationToken);
            }

            var pending = await _journals.GetPendingAsync(cancellationToken);
            var journalSummary = pending.Select(item => new
            {
                item.OperationId,
                item.OperationType,
                item.TransactionId,
                item.Stage,
                item.Status,
                item.StartedAt,
                item.UpdatedAt,
                SourcePdfPath = SanitizePath(item.SourcePdfPath),
                DestinationPdfPath = SanitizePath(item.DestinationPdfPath),
                LastError = Truncate(SanitizeText(item.LastError), 8192),
                item.RetryCount
            });
            await WriteJsonAsync(
                Path.Combine(staging, "pending-workflows.json"),
                journalSummary,
                cancellationToken);

            if (_preflight.LastReport is not null)
            {
                await WriteJsonAsync(
                    Path.Combine(staging, "last-preflight.json"),
                    new
                    {
                        _preflight.LastReport.CompletedAt,
                        Checks = _preflight.LastReport.Checks.Select(check => new
                        {
                            check.Name,
                            check.Status,
                            Message = SanitizeText(check.Message),
                            check.IsBlocking
                        })
                    },
                    cancellationToken);
            }

            var logDestination = Path.Combine(staging, "Logs");
            Directory.CreateDirectory(logDestination);
            foreach (var log in Directory.EnumerateFiles(_paths.LogDirectory, "equipment-tracking-*.log")
                         .Select(path => new FileInfo(path))
                         .OrderByDescending(file => file.LastWriteTimeUtc)
                         .Take(5))
            {
                var rawLog = await File.ReadAllTextAsync(log.FullName, cancellationToken);
                await File.WriteAllTextAsync(
                    Path.Combine(logDestination, log.Name),
                    SanitizeText(rawLog),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken);
            }

            ZipFile.CreateFromDirectory(staging, destination, CompressionLevel.Optimal, includeBaseDirectory: false);
            _logger.Information($"Created sanitized support package: {destination}");
            return destination;
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    private static async Task WriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
    }

    private static string SanitizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var result = value;
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            result = result.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            result = result.Replace(localAppData, "%LOCALAPPDATA%", StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }

    private static string SanitizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var result = value;
        var replacements = new (string Value, string Token)[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
            (Environment.UserName, "%USERNAME%"),
            (Environment.MachineName, "%COMPUTERNAME%")
        };

        foreach (var replacement in replacements)
        {
            if (!string.IsNullOrWhiteSpace(replacement.Value))
            {
                result = result.Replace(
                    replacement.Value,
                    replacement.Token,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        // Remaining absolute local and UNC paths can contain customer names,
        // ticket numbers, or organization names in PDF filenames. Keep only a
        // marker in support packages; the live application log remains untouched.
        result = Regex.Replace(
            result,
            @"(?i)%(?:USERPROFILE|LOCALAPPDATA)%\\[^\r\n]+",
            "%PROFILE_PATH%");
        result = Regex.Replace(
            result,
            @"(?i)(?:[a-z]:\\|\\\\)[^\r\n]+",
            "%PATH%");
        return result;
    }

    private static string BuildNonReversibleIdentifier(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes)[..12];
    }

    private static string Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maximumLength)
        {
            return value ?? string.Empty;
        }
        return value[..maximumLength] + " [truncated]";
    }
}
