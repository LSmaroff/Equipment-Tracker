using System.IO.Compression;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;
using Microsoft.Data.Sqlite;

namespace EquipmentTracking.Tests;

public sealed class ReleaseReadinessIntegrationTests
{
    [Fact]
    public async Task ResetOperationalDataClearsRecordsButPreservesSchema()
    {
        SQLitePCL.Batteries_V2.Init();
        var folder = CreateTemporaryFolder();
        try
        {
            var paths = new AppPaths(folder);
            var logger = new FileLogger(paths);
            var backups = new BackupService(paths, logger);
            var database = new DatabaseService(paths, logger, backups);
            await database.InitializeAsync(createAutomaticBackup: false);

            Assert.True(await database.InsertTransactionAsync(BuildTransaction("TX-RESET")));
            await database.RecordFileArtifactAsync(
                "TX-RESET",
                "WorkingStatusCopy",
                Path.Combine(folder, "test.pdf"),
                "ABC123",
                100);

            await database.ResetOperationalDataAsync();

            Assert.Null(await database.GetTransactionByIdAsync("TX-RESET"));
            Assert.Empty(await database.GetFileArtifactsAsync("TX-RESET"));
            Assert.Equal(DatabaseService.CurrentSchemaVersion, await database.GetCurrentSchemaVersionAsync());
            Assert.Equal("ok", await database.VerifyIntegrityAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryFolder(folder);
        }
    }

    [Fact]
    public async Task PendingRestoreIsReverifiedBeforeReplacingCurrentDatabase()
    {
        SQLitePCL.Batteries_V2.Init();
        var folder = CreateTemporaryFolder();
        try
        {
            var paths = new AppPaths(folder);
            var logger = new FileLogger(paths);
            var backups = new BackupService(paths, logger);
            var database = new DatabaseService(paths, logger, backups);
            await database.InitializeAsync(createAutomaticBackup: false);
            Assert.True(await database.InsertTransactionAsync(BuildTransaction("TX-ORIGINAL")));

            var backupPath = await backups.CreateBackupAsync(
                "restore-test",
                DatabaseService.CurrentSchemaVersion,
                includeSettings: false);
            await backups.StageRestoreAsync(backupPath, restoreSettings: false);

            var stagedDatabase = Path.Combine(
                paths.PendingRestoreDirectory,
                "equipment-tracking.db");
            await File.AppendAllTextAsync(stagedDatabase, "tampered");

            await Assert.ThrowsAsync<InvalidDataException>(
                () => backups.ApplyPendingRestoreIfAnyAsync());

            Assert.NotNull(await database.GetTransactionByIdAsync("TX-ORIGINAL"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryFolder(folder);
        }
    }

    [Fact]
    public async Task PreflightAcceptsTheIncludedCleanTemplateWithoutBlockingFailures()
    {
        SQLitePCL.Batteries_V2.Init();
        var folder = CreateTemporaryFolder();
        try
        {
            var template = GetIncludedTemplatePath();
            var paths = new AppPaths(folder);
            var logger = new FileLogger(paths);
            var settings = new SettingsService(paths, logger);
            await settings.SaveAsync(new AppSettings
            {
                TemplatePdfPath = template,
                CompletedPdfFolder = Path.Combine(folder, "Completed"),
                ExcelExportPath = Path.Combine(folder, "Reports", "EquipmentTracking.xlsx")
            });

            var backups = new BackupService(paths, logger);
            var database = new DatabaseService(paths, logger, backups);
            await database.InitializeAsync(createAutomaticBackup: false);
            var pdfForms = new PdfFormService(logger);
            var certificates = new CacCertificateService(
                new CertificateNameParser(),
                logger);
            var preflight = new PreflightService(
                paths,
                settings,
                database,
                pdfForms,
                certificates,
                logger);

            var report = await preflight.RunAsync();

            Assert.False(report.HasBlockingFailures);
            var templateResult = Assert.Single(
                report.Checks,
                check => check.Name == "1297 template");
            Assert.Equal(PreflightStatus.Passed, templateResult.Status);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryFolder(folder);
        }
    }

    [Fact]
    public async Task SupportPackageOmitsDatabaseAndRedactsLocalIdentityAndPaths()
    {
        SQLitePCL.Batteries_V2.Init();
        var folder = CreateTemporaryFolder();
        try
        {
            var paths = new AppPaths(folder);
            var logger = new FileLogger(paths);
            var settings = new SettingsService(paths, logger);
            await settings.SaveAsync(new AppSettings
            {
                TemplatePdfPath = GetIncludedTemplatePath(),
                CompletedPdfFolder = Path.Combine(folder, "Completed"),
                ExcelExportPath = Path.Combine(folder, "Reports", "EquipmentTracking.xlsx")
            });

            var backups = new BackupService(paths, logger);
            var database = new DatabaseService(paths, logger, backups);
            await database.InitializeAsync(createAutomaticBackup: false);
            var pdfForms = new PdfFormService(logger);
            var certificates = new CacCertificateService(
                new CertificateNameParser(),
                logger);
            var journals = new WorkflowJournalService(paths, logger);
            var preflight = new PreflightService(
                paths,
                settings,
                database,
                pdfForms,
                certificates,
                logger);
            var packages = new SupportPackageService(
                paths,
                settings,
                database,
                pdfForms,
                certificates,
                journals,
                preflight,
                logger);

            const string sensitiveFileName = "SensitiveCustomerName-INC987654.pdf";
            logger.Error(
                "Diagnostic test path: " + Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Documents",
                    sensitiveFileName),
                new IOException("Synthetic test exception."));

            var packagePath = await packages.CreateAsync();
            using var archive = ZipFile.OpenRead(packagePath);
            Assert.DoesNotContain(
                archive.Entries,
                entry => entry.FullName.EndsWith(
                    "equipment-tracking.db",
                    StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                archive.Entries,
                entry => entry.FullName.EndsWith(
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase));

            var combinedText = string.Empty;
            foreach (var entry in archive.Entries.Where(entry => entry.Length > 0))
            {
                using var reader = new StreamReader(entry.Open());
                combinedText += await reader.ReadToEndAsync();
            }

            Assert.False(combinedText.Contains(
                sensitiveFileName,
                StringComparison.OrdinalIgnoreCase));
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                Assert.False(combinedText.Contains(
                    userProfile,
                    StringComparison.OrdinalIgnoreCase));
            }
            Assert.True(
                combinedText.Contains("%PROFILE_PATH%", StringComparison.Ordinal) ||
                combinedText.Contains("%PATH%", StringComparison.Ordinal));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryFolder(folder);
        }
    }

    private static EquipmentTransaction BuildTransaction(string id) => new()
    {
        Id = id,
        Customer = new CustomerIdentity
        {
            Rank = "A1C",
            FirstName = "Test",
            LastName = "User",
            OriginalName = "TEST.USER"
        },
        PhoneNumber = "555-0100",
        Technician = "SSgt Tester",
        Organization = "58 SOW",
        TicketNumber = id,
        IssuedAt = DateTimeOffset.Now,
        CreatedAt = DateTimeOffset.Now,
        PdfPath = Path.Combine(Path.GetTempPath(), id + ".pdf"),
        PdfSignerName = "Test User",
        Devices =
        [
            new DeviceRecord
            {
                Model = "MODEL-1",
                SerialNumber = id + "-SERIAL",
                Status = DeviceStatusCatalog.InShop
            }
        ]
    };

    private static string GetIncludedTemplatePath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Templates",
            "1297-58SOW-SC-TEMPLATE.pdf");

    private static string CreateTemporaryFolder()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "EquipmentTrackingTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTemporaryFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Cleanup must not hide the test result.
        }
    }
}
