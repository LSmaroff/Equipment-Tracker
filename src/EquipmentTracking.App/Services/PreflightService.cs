using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using Microsoft.Win32;

namespace EquipmentTracking.App.Services;

public sealed class PreflightService
{
    private const string ApprovedTemplateSha256 =
        "00daa4ac652d592f03554e1d8e2ea9ec6083b30e9c9659330fe1dad32e599051";

    private readonly AppPaths _paths;
    private readonly SettingsService _settings;
    private readonly DatabaseService _database;
    private readonly PdfFormService _pdfForms;
    private readonly CacCertificateService _cacCertificates;
    private readonly FileLogger _logger;

    public PreflightService(
        AppPaths paths,
        SettingsService settings,
        DatabaseService database,
        PdfFormService pdfForms,
        CacCertificateService cacCertificates,
        FileLogger logger)
    {
        _paths = paths;
        _settings = settings;
        _database = database;
        _pdfForms = pdfForms;
        _cacCertificates = cacCertificates;
        _logger = logger;
    }

    public PreflightReport? LastReport { get; private set; }

    public async Task<PreflightReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<PreflightCheckResult>();
        checks.Add(CheckWritableDirectory(
            "Application data folder",
            _paths.BaseDataDirectory,
            isBlocking: true));
        checks.Add(CheckWritableDirectory(
            "Working PDF folder",
            _paths.WorkingDirectory,
            isBlocking: true));
        checks.Add(CheckWritableDirectory(
            "Temporary print-job folder",
            _paths.PrintJobsDirectory,
            isBlocking: false));

        try
        {
            var integrity = await _database.VerifyIntegrityAsync(cancellationToken);
            checks.Add(new PreflightCheckResult
            {
                Name = "SQLite database",
                Status = string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase)
                    ? PreflightStatus.Passed
                    : PreflightStatus.Failed,
                Message = string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase)
                    ? "Database opened successfully and PRAGMA quick_check returned ok."
                    : $"SQLite integrity result: {integrity}",
                IsBlocking = true
            });
        }
        catch (Exception ex)
        {
            checks.Add(Failed("SQLite database", ex.Message, isBlocking: true));
        }

        var templatePath = _settings.ResolvePath(_settings.Current.TemplatePdfPath);
        try
        {
            var requiredFields = _settings.Current.PdfFieldMappings.Values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var inspection = _pdfForms.InspectTemplate(templatePath, requiredFields);
            var problems = new List<string>();
            if (inspection.MissingRequiredFields.Count > 0)
            {
                problems.Add("Missing fields: " + string.Join(", ", inspection.MissingRequiredFields));
            }
            if (inspection.NeedAppearances)
            {
                problems.Add("NeedAppearances is enabled");
            }
            if (inspection.ContainsJavaScriptMarkers)
            {
                problems.Add("JavaScript markers were detected");
            }
            if (inspection.SignatureFieldCount < 3)
            {
                problems.Add($"Only {inspection.SignatureFieldCount} signature field(s) were detected");
            }
            using (var templateStream = File.OpenRead(templatePath))
            {
                var templateHash = Convert.ToHexString(
                        SHA256.HashData(templateStream))
                    .ToLowerInvariant();
                if (!string.Equals(
                        templateHash,
                        ApprovedTemplateSha256,
                        StringComparison.Ordinal))
                {
                    problems.Add(
                        $"SHA-256 does not match the approved cleaned template ({templateHash})");
                }
            }

            checks.Add(new PreflightCheckResult
            {
                Name = "1297 template",
                Status = problems.Count == 0 ? PreflightStatus.Passed : PreflightStatus.Failed,
                Message = problems.Count == 0
                    ? $"Template is readable with {inspection.FieldNames.Count} fields, " +
                      $"{inspection.SignatureFieldCount} signature fields, and the approved SHA-256."
                    : string.Join("; ", problems),
                IsBlocking = false
            });
        }
        catch (Exception ex)
        {
            checks.Add(Failed("1297 template", ex.Message, isBlocking: false));
        }

        var completedFolder = _settings.ResolvePath(_settings.Current.CompletedPdfFolder);
        checks.Add(CheckWritableDirectory(
            "Completed PDF folder",
            completedFolder,
            isBlocking: false));
        checks.Add(CheckScheduledBackupLocation(completedFolder));

        var excelPath = _settings.ResolvePath(_settings.Current.ExcelExportPath);
        var excelDirectory = Path.GetDirectoryName(excelPath) ?? string.Empty;
        checks.Add(CheckWritableDirectory(
            "Excel export folder",
            excelDirectory,
            isBlocking: false));

        checks.Add(CheckPdfApplication());
        checks.Add(CheckSmartCardReaders());
        checks.Add(CheckDiskSpace());

        LastReport = new PreflightReport
        {
            CompletedAt = DateTimeOffset.Now,
            Checks = checks
        };
        _logger.Information(
            $"Startup preflight completed: {LastReport.PassedCount} passed, " +
            $"{LastReport.WarningCount} warning(s), {LastReport.FailedCount} failed.");
        return LastReport;
    }

    private static PreflightCheckResult CheckWritableDirectory(
        string name,
        string directory,
        bool isBlocking)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("No folder is configured.");
            }

            Directory.CreateDirectory(directory);
            var testPath = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testPath, "test");
            File.Delete(testPath);
            return new PreflightCheckResult
            {
                Name = name,
                Status = PreflightStatus.Passed,
                Message = directory,
                IsBlocking = isBlocking
            };
        }
        catch (Exception ex)
        {
            return Failed(name, ex.Message, isBlocking);
        }
    }

    private PreflightCheckResult CheckPdfApplication()
    {
        var configured = _settings.ResolvePath(_settings.Current.AdobeExecutablePath);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return File.Exists(configured)
                ? new PreflightCheckResult
                {
                    Name = "PDF signing application",
                    Status = PreflightStatus.Passed,
                    Message = configured
                }
                : Failed(
                    "PDF signing application",
                    $"The configured Adobe executable was not found: {configured}",
                    isBlocking: false);
        }

        try
        {
            using var pdfAssociation = Registry.ClassesRoot.OpenSubKey(".pdf");
            var association = Convert.ToString(pdfAssociation?.GetValue(null), CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(association)
                ? new PreflightCheckResult
                {
                    Name = "PDF signing application",
                    Status = PreflightStatus.Warning,
                    Message = "No explicit Adobe path is configured and Windows did not report a default PDF association."
                }
                : new PreflightCheckResult
                {
                    Name = "PDF signing application",
                    Status = PreflightStatus.Passed,
                    Message = $"Windows PDF association: {association}. Verify that the approved Adobe version supports CAC signatures."
                };
        }
        catch (Exception ex)
        {
            return new PreflightCheckResult
            {
                Name = "PDF signing application",
                Status = PreflightStatus.Warning,
                Message = $"The Windows PDF association could not be inspected: {ex.Message}"
            };
        }
    }

    private PreflightCheckResult CheckSmartCardReaders()
    {
        try
        {
            var readers = _cacCertificates.GetReaderNamesForDiagnostics();
            if (readers.Count == 0)
            {
                return new PreflightCheckResult
                {
                    Name = "Smart-card readers",
                    Status = PreflightStatus.Warning,
                    Message = "The Windows Smart Card service responded, but no reader is currently available. CAC insertion is not required to start the application."
                };
            }

            var inserted = _cacCertificates.GetReaderNamesForDiagnostics(insertedCardsOnly: true);
            return new PreflightCheckResult
            {
                Name = "Smart-card readers",
                Status = PreflightStatus.Passed,
                Message = inserted.Count > 0
                    ? $"{readers.Count} reader(s) detected; {inserted.Count} currently contain a card."
                    : $"{readers.Count} reader(s) detected and ready; no CAC is currently inserted."
            };
        }
        catch (Exception ex)
        {
            return new PreflightCheckResult
            {
                Name = "Smart-card readers",
                Status = PreflightStatus.Warning,
                Message = ex.Message
            };
        }
    }

    private PreflightCheckResult CheckDiskSpace()
    {
        try
        {
            var root = Path.GetPathRoot(_paths.BaseDataDirectory)
                ?? throw new InvalidOperationException("The data drive could not be determined.");
            var drive = new DriveInfo(root);
            const long minimumBytes = 500L * 1024L * 1024L;
            return new PreflightCheckResult
            {
                Name = "Available disk space",
                Status = drive.AvailableFreeSpace >= minimumBytes
                    ? PreflightStatus.Passed
                    : PreflightStatus.Warning,
                Message = $"{drive.AvailableFreeSpace / (1024d * 1024d):0} MB free on {drive.Name}."
            };
        }
        catch (Exception ex)
        {
            return new PreflightCheckResult
            {
                Name = "Available disk space",
                Status = PreflightStatus.Warning,
                Message = ex.Message
            };
        }
    }

    private PreflightCheckResult CheckScheduledBackupLocation(string completedFolder)
    {
        if (!_settings.Current.ScheduledBackupsEnabled)
        {
            return new PreflightCheckResult
            {
                Name = "Scheduled backup destination",
                Status = PreflightStatus.Warning,
                Message = "Scheduled backups are disabled. Enable them before operational use."
            };
        }

        try
        {
            var backupFolder = _settings.ResolvePath(_settings.Current.ScheduledBackupFolder);
            var writable = CheckWritableDirectory(
                "Scheduled backup destination",
                backupFolder,
                isBlocking: false);
            if (writable.Status == PreflightStatus.Failed)
            {
                return writable;
            }

            if (PathsOverlap(backupFolder, completedFolder) ||
                PathsOverlap(backupFolder, _paths.BaseDataDirectory))
            {
                return new PreflightCheckResult
                {
                    Name = "Scheduled backup destination",
                    Status = PreflightStatus.Failed,
                    Message = "The backup folder must be separate from both the application-data and completed-PDF folders. " +
                              backupFolder,
                    IsBlocking = false
                };
            }

            var backupRoot = Path.GetPathRoot(Path.GetFullPath(backupFolder));
            var dataRoot = Path.GetPathRoot(Path.GetFullPath(_paths.BaseDataDirectory));
            var completedRoot = Path.GetPathRoot(Path.GetFullPath(completedFolder));
            var warnings = new List<string>();
            var sharesDataDrive = string.Equals(
                backupRoot,
                dataRoot,
                StringComparison.OrdinalIgnoreCase);
            var sharesCompletedDrive = string.Equals(
                backupRoot,
                completedRoot,
                StringComparison.OrdinalIgnoreCase);
            if (sharesDataDrive || sharesCompletedDrive)
            {
                var protectedLocations = sharesDataDrive && sharesCompletedDrive
                    ? "the database and 1297 records"
                    : sharesDataDrive
                        ? "the database"
                        : "the 1297 records";
                warnings.Add(
                    $"It is on the same drive/share as {protectedLocations}, so it does not fully protect against drive failure.");
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(backupRoot))
                {
                    var drive = new DriveInfo(backupRoot);
                    if (drive.IsReady)
                    {
                        if (string.Equals(drive.DriveFormat, "FAT32", StringComparison.OrdinalIgnoreCase))
                        {
                            warnings.Add(
                                "The destination is FAT32, whose 4 GB single-file limit can prevent a large full backup.");
                        }

                        const long minimumBackupFreeBytes = 5L * 1024L * 1024L * 1024L;
                        if (drive.AvailableFreeSpace < minimumBackupFreeBytes)
                        {
                            warnings.Add(
                                $"Only {drive.AvailableFreeSpace / (1024d * 1024d):0} MB is free; verify capacity for the database and all protected PDFs.");
                        }
                    }
                }
            }
            catch
            {
                // Some approved network providers do not expose drive format or
                // free-space metadata. The write test above remains authoritative.
            }

            if (warnings.Count > 0)
            {
                return new PreflightCheckResult
                {
                    Name = "Scheduled backup destination",
                    Status = PreflightStatus.Warning,
                    Message = backupFolder + " — writable. " + string.Join(" ", warnings)
                };
            }

            return new PreflightCheckResult
            {
                Name = "Scheduled backup destination",
                Status = PreflightStatus.Passed,
                Message = backupFolder
            };
        }
        catch (Exception ex)
        {
            return Failed("Scheduled backup destination", ex.Message, isBlocking: false);
        }
    }

    private static bool PathsOverlap(string first, string second)
    {
        var firstFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(first));
        var secondFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(second));
        return IsSameOrChild(firstFull, secondFull) || IsSameOrChild(secondFull, firstFull);
    }

    private static bool IsSameOrChild(string path, string possibleParent) =>
        string.Equals(path, possibleParent, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(possibleParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static PreflightCheckResult Failed(
        string name,
        string message,
        bool isBlocking)
    {
        return new PreflightCheckResult
        {
            Name = name,
            Status = PreflightStatus.Failed,
            Message = message,
            IsBlocking = isBlocking
        };
    }
}
