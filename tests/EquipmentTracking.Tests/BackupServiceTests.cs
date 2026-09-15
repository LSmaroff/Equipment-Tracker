using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;
using Microsoft.Data.Sqlite;

namespace EquipmentTracking.Tests;

public sealed class BackupServiceTests
{
    [Fact]
    public async Task BackupContainsVerifiedDatabaseManifestAndSettings()
    {
        SQLitePCL.Batteries_V2.Init();
        var folder = Path.Combine(Path.GetTempPath(), "EquipmentTrackingTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(folder);
            var logger = new FileLogger(paths);
            var backups = new BackupService(paths, logger);
            var database = new DatabaseService(paths, logger, backups);
            await database.InitializeAsync(createAutomaticBackup: false);
            await File.WriteAllTextAsync(paths.SettingsPath, "{}");

            var backupPath = await backups.CreateBackupAsync(
                "test",
                DatabaseService.CurrentSchemaVersion,
                includeSettings: true);

            Assert.True(File.Exists(backupPath));
            using var archive = ZipFile.OpenRead(backupPath);
            Assert.NotNull(archive.GetEntry("equipment-tracking.db"));
            Assert.NotNull(archive.GetEntry("settings.json"));
            Assert.NotNull(archive.GetEntry("backup-manifest.json"));

            await backups.StageRestoreAsync(backupPath, restoreSettings: true);
            Assert.True(File.Exists(paths.PendingRestoreMarkerPath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task ScheduledFullAndDifferentialProtectRecordFilesAndRestoreTogether()
    {
        SQLitePCL.Batteries_V2.Init();
        var folder = Path.Combine(Path.GetTempPath(), "EquipmentTrackingTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(Path.Combine(folder, "data"));
            var logger = new FileLogger(paths);
            var backups = new BackupService(paths, logger);
            var database = new DatabaseService(paths, logger, backups);
            await database.InitializeAsync(createAutomaticBackup: false);
            await File.WriteAllTextAsync(paths.SettingsPath, "{}");

            var records = Path.Combine(folder, "records");
            var scheduled = Path.Combine(folder, "scheduled");
            Directory.CreateDirectory(Path.Combine(records, "Original Signed Intake"));
            await File.WriteAllTextAsync(Path.Combine(records, "active.pdf"), "active-v1");
            await File.WriteAllTextAsync(
                Path.Combine(records, "Original Signed Intake", "original.pdf"),
                "original");

            var full = await backups.CreateScheduledFullBackupAsync(
                scheduled,
                records,
                DatabaseService.CurrentSchemaVersion);
            Assert.True(File.Exists(full.BackupPath));
            Assert.True(File.Exists(full.BackupPath + ".sha256"));
            Assert.Equal(2, full.RecordFileCount);

            await File.WriteAllTextAsync(Path.Combine(records, "active.pdf"), "active-v2");
            await File.WriteAllTextAsync(Path.Combine(records, "new.pdf"), "new-record");
            var differential = await backups.CreateScheduledDifferentialBackupAsync(
                scheduled,
                records,
                DatabaseService.CurrentSchemaVersion,
                full.BackupPath);
            Assert.Equal(2, differential.RecordFileCount);

            var restoreTarget = Path.Combine(folder, "restored-records");
            await backups.StageRestoreAsync(
                differential.BackupPath,
                restoreSettings: true,
                targetCompletedPdfRoot: restoreTarget);
            SqliteConnection.ClearAllPools();
            await backups.ApplyPendingRestoreIfAnyAsync();

            Assert.Equal("active-v2", await File.ReadAllTextAsync(Path.Combine(restoreTarget, "active.pdf")));
            Assert.Equal("new-record", await File.ReadAllTextAsync(Path.Combine(restoreTarget, "new.pdf")));
            Assert.Equal(
                "original",
                await File.ReadAllTextAsync(
                    Path.Combine(restoreTarget, "Original Signed Intake", "original.pdf")));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task DifferentialRestoreDoesNotReintroduceARecordDeletedAfterTheFull()
    {
        SQLitePCL.Batteries_V2.Init();
        var folder = Path.Combine(Path.GetTempPath(), "EquipmentTrackingTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(Path.Combine(folder, "data"));
            var logger = new FileLogger(paths);
            var backups = new BackupService(paths, logger);
            var database = new DatabaseService(paths, logger, backups);
            await database.InitializeAsync(createAutomaticBackup: false);

            var records = Path.Combine(folder, "records");
            var scheduled = Path.Combine(folder, "scheduled");
            Directory.CreateDirectory(records);
            var removedPath = Path.Combine(records, "moved-from-active.pdf");
            await File.WriteAllTextAsync(removedPath, "old-location");

            var full = await backups.CreateScheduledFullBackupAsync(
                scheduled,
                records,
                DatabaseService.CurrentSchemaVersion);
            File.Delete(removedPath);
            await File.WriteAllTextAsync(Path.Combine(records, "archived.pdf"), "new-location");

            var differential = await backups.CreateScheduledDifferentialBackupAsync(
                scheduled,
                records,
                DatabaseService.CurrentSchemaVersion,
                full.BackupPath);
            var differentialManifest = await backups.VerifyBackupAsync(differential.BackupPath);
            Assert.Contains("moved-from-active.pdf", differentialManifest.DeletedRecordPaths);

            var restoreTarget = Path.Combine(folder, "restored-records");
            await backups.StageRestoreAsync(
                differential.BackupPath,
                restoreSettings: false,
                targetCompletedPdfRoot: restoreTarget);
            SqliteConnection.ClearAllPools();
            await backups.ApplyPendingRestoreIfAnyAsync();

            Assert.False(File.Exists(Path.Combine(restoreTarget, "moved-from-active.pdf")));
            Assert.Equal(
                "new-location",
                await File.ReadAllTextAsync(Path.Combine(restoreTarget, "archived.pdf")));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task ScheduledFullStopsWhenTheDatabaseReferencesAMissingOfficialPdf()
    {
        SQLitePCL.Batteries_V2.Init();
        var folder = Path.Combine(Path.GetTempPath(), "EquipmentTrackingTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(Path.Combine(folder, "data"));
            var logger = new FileLogger(paths);
            var backups = new BackupService(paths, logger);
            var database = new DatabaseService(paths, logger, backups);
            await database.InitializeAsync(createAutomaticBackup: false);
            var records = Path.Combine(folder, "records");
            var scheduled = Path.Combine(folder, "scheduled");
            Directory.CreateDirectory(records);
            var missingPath = Path.Combine(records, "missing.pdf");
            Assert.True(await database.InsertTransactionAsync(new EquipmentTransaction
            {
                Id = "TX-MISSING-PDF",
                Customer = new CustomerIdentity
                {
                    FirstName = "Synthetic",
                    LastName = "Record",
                    OriginalName = "SYNTHETIC.RECORD"
                },
                IssuedAt = DateTimeOffset.Now,
                CreatedAt = DateTimeOffset.Now,
                PdfPath = missingPath
            }));

            var error = await Assert.ThrowsAsync<FileNotFoundException>(() =>
                backups.CreateScheduledFullBackupAsync(
                    scheduled,
                    records,
                    DatabaseService.CurrentSchemaVersion));

            Assert.Equal(missingPath, error.FileName);
            Assert.Empty(Directory.GetFiles(scheduled, "*.zip"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task ScheduledBackupProtectsAndRemapsSignedPartialPickupReceipts()
    {
        SQLitePCL.Batteries_V2.Init();
        var folder = Path.Combine(
            Path.GetTempPath(),
            "EquipmentTrackingTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(Path.Combine(folder, "data"));
            var logger = new FileLogger(paths);
            var backups = new BackupService(paths, logger);
            var database = new DatabaseService(paths, logger, backups);
            await database.InitializeAsync(createAutomaticBackup: false);

            var records = Path.GetFullPath(Path.Combine(folder, "records"));
            var scheduled = Path.Combine(folder, "scheduled");
            var parentPdf = Path.Combine(records, "active-parent.pdf");
            var receiptPdf = Path.Combine(
                records,
                "Pickup receipts",
                "TX-BACKUP-PICKUP",
                "pickup-001.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(receiptPdf)!);
            await File.WriteAllTextAsync(parentPdf, "synthetic active parent");
            await File.WriteAllTextAsync(receiptPdf, "synthetic signed partial pickup");
            var transaction = new EquipmentTransaction
            {
                Id = "TX-BACKUP-PICKUP",
                Customer = new CustomerIdentity
                {
                    FirstName = "Synthetic",
                    LastName = "Customer",
                    OriginalName = "SYNTHETIC.CUSTOMER"
                },
                Technician = "SSgt Synthetic",
                Organization = "TEST ORG",
                IssuedAt = DateTimeOffset.Now,
                CreatedAt = DateTimeOffset.Now,
                PdfPath = parentPdf,
                Devices =
                [
                    new DeviceRecord
                    {
                        PartNumber = "PART-1",
                        Model = "MODEL-1",
                        SerialNumber = "BACKUP-PICKUP-1",
                        Status = DeviceStatusCatalog.InShop
                    },
                    new DeviceRecord
                    {
                        PartNumber = "PART-2",
                        Model = "MODEL-2",
                        SerialNumber = "BACKUP-PICKUP-2",
                        Status = DeviceStatusCatalog.InShop
                    }
                ]
            };
            Assert.True(await database.InsertTransactionAsync(transaction));
            var receiptBytes = await File.ReadAllBytesAsync(receiptPdf);
            await database.CommitPickupReceiptAsync(new PickupReceipt
            {
                Id = "pickup-backup-1",
                TransactionId = transaction.Id,
                SequenceNumber = 1,
                PdfPath = receiptPdf,
                Technician = "SSgt Synthetic",
                Notes = "Synthetic backup coverage.",
                PickedUpAt = DateTimeOffset.Now,
                Sha256 = Convert.ToHexString(SHA256.HashData(receiptBytes)),
                SizeBytes = receiptBytes.LongLength,
                SignerName = "Synthetic Customer",
                SignatureTime = DateTimeOffset.Now,
                DeviceIds = [transaction.Devices[0].Id]
            });

            var full = await backups.CreateScheduledFullBackupAsync(
                scheduled,
                records,
                DatabaseService.CurrentSchemaVersion);
            var restoreTarget = Path.GetFullPath(Path.Combine(folder, "restored-records"));
            await backups.StageRestoreAsync(
                full.BackupPath,
                restoreSettings: false,
                targetCompletedPdfRoot: restoreTarget);
            SqliteConnection.ClearAllPools();
            await backups.ApplyPendingRestoreIfAnyAsync();

            var restoredReceipt = Assert.Single(
                await database.GetPickupReceiptsAsync(transaction.Id));
            var expectedRestoredPath = Path.Combine(
                restoreTarget,
                "Pickup receipts",
                transaction.Id,
                "pickup-001.pdf");
            Assert.Equal(expectedRestoredPath, restoredReceipt.PdfPath);
            Assert.True(File.Exists(restoredReceipt.PdfPath));

            File.Delete(restoredReceipt.PdfPath);
            var error = await Assert.ThrowsAsync<FileNotFoundException>(() =>
                backups.CreateScheduledFullBackupAsync(
                    Path.Combine(folder, "scheduled-after-restore"),
                    restoreTarget,
                    DatabaseService.CurrentSchemaVersion));
            Assert.Equal(restoredReceipt.PdfPath, error.FileName);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task BackupVerificationRejectsWindowsAlternateDataStreamRecordPaths()
    {
        SQLitePCL.Batteries_V2.Init();
        var folder = Path.Combine(Path.GetTempPath(), "EquipmentTrackingTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(Path.Combine(folder, "data"));
            var logger = new FileLogger(paths);
            var backups = new BackupService(paths, logger);
            var archivePath = Path.Combine(folder, "unsafe-record-path.zip");
            Directory.CreateDirectory(folder);

            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var manifestEntry = archive.CreateEntry("backup-manifest.json");
                await using var stream = manifestEntry.Open();
                await JsonSerializer.SerializeAsync(stream, new BackupManifest
                {
                    FormatVersion = BackupManifest.CurrentFormatVersion,
                    BackupId = Guid.NewGuid().ToString("N"),
                    BackupType = BackupManifest.FullBackupType,
                    DatabaseSchemaVersion = DatabaseService.CurrentSchemaVersion,
                    DatabaseSha256 = new string('0', 64),
                    CompletedPdfRoot = Path.GetFullPath(Path.Combine(folder, "records")),
                    RecordFiles =
                    [
                        new BackupFileEntry
                        {
                            RelativePath = "record.pdf:alternate.pdf",
                            ArchiveEntryName = "records/record.pdf:alternate.pdf",
                            Sha256 = new string('0', 64),
                            SizeBytes = 1
                        }
                    ]
                });
            }

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => backups.VerifyBackupAsync(archivePath));
            Assert.Contains("unsafe relative PDF path", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task NewVerifiedFullRemovesOldDifferentialsAndHonorsFullRetention()
    {
        SQLitePCL.Batteries_V2.Init();
        var folder = Path.Combine(Path.GetTempPath(), "EquipmentTrackingTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(Path.Combine(folder, "data"));
            var logger = new FileLogger(paths);
            var backups = new BackupService(paths, logger);
            var database = new DatabaseService(paths, logger, backups);
            await database.InitializeAsync(createAutomaticBackup: false);
            await File.WriteAllTextAsync(paths.SettingsPath, "{}");
            var records = Path.Combine(folder, "records");
            var scheduled = Path.Combine(folder, "scheduled");
            Directory.CreateDirectory(records);
            await File.WriteAllTextAsync(Path.Combine(records, "record.pdf"), "week-one");

            var firstFull = await backups.CreateScheduledFullBackupAsync(
                scheduled,
                records,
                DatabaseService.CurrentSchemaVersion);
            await File.WriteAllTextAsync(Path.Combine(records, "record.pdf"), "week-one-change");
            var oldDifferential = await backups.CreateScheduledDifferentialBackupAsync(
                scheduled,
                records,
                DatabaseService.CurrentSchemaVersion,
                firstFull.BackupPath);

            await File.WriteAllTextAsync(Path.Combine(records, "record.pdf"), "week-two");
            var secondFull = await backups.CreateScheduledFullBackupAsync(
                scheduled,
                records,
                DatabaseService.CurrentSchemaVersion);
            await backups.ApplyScheduledRetentionAsync(
                scheduled,
                secondFull.BackupId,
                maximumFullBackupCount: 1);

            Assert.False(File.Exists(oldDifferential.BackupPath));
            Assert.False(File.Exists(firstFull.BackupPath));
            Assert.True(File.Exists(secondFull.BackupPath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task RetentionPreservesPriorBackupsWhenTheNewFullIsCorrupt()
    {
        SQLitePCL.Batteries_V2.Init();
        var folder = Path.Combine(Path.GetTempPath(), "EquipmentTrackingTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(Path.Combine(folder, "data"));
            var logger = new FileLogger(paths);
            var backups = new BackupService(paths, logger);
            var database = new DatabaseService(paths, logger, backups);
            await database.InitializeAsync(createAutomaticBackup: false);
            var records = Path.Combine(folder, "records");
            var scheduled = Path.Combine(folder, "scheduled");
            Directory.CreateDirectory(records);
            await File.WriteAllTextAsync(Path.Combine(records, "record.pdf"), "week-one");

            var firstFull = await backups.CreateScheduledFullBackupAsync(
                scheduled,
                records,
                DatabaseService.CurrentSchemaVersion);
            await File.WriteAllTextAsync(Path.Combine(records, "record.pdf"), "week-one-change");
            var oldDifferential = await backups.CreateScheduledDifferentialBackupAsync(
                scheduled,
                records,
                DatabaseService.CurrentSchemaVersion,
                firstFull.BackupPath);
            var secondFull = await backups.CreateScheduledFullBackupAsync(
                scheduled,
                records,
                DatabaseService.CurrentSchemaVersion);

            using (var archive = ZipFile.Open(secondFull.BackupPath, ZipArchiveMode.Update))
            {
                var databaseEntry = archive.GetEntry("equipment-tracking.db");
                Assert.NotNull(databaseEntry);
                databaseEntry!.Delete();
                var replacement = archive.CreateEntry("equipment-tracking.db");
                await using var writer = new StreamWriter(replacement.Open());
                await writer.WriteAsync("tampered");
            }

            await Assert.ThrowsAnyAsync<Exception>(() =>
                backups.ApplyScheduledRetentionAsync(
                    scheduled,
                    secondFull.BackupId,
                    maximumFullBackupCount: 1));

            Assert.True(File.Exists(firstFull.BackupPath));
            Assert.True(File.Exists(oldDifferential.BackupPath));
            Assert.True(File.Exists(secondFull.BackupPath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task SchedulerCreatesMondayFullThenWeekdayDifferentials()
    {
        SQLitePCL.Batteries_V2.Init();
        var folder = Path.Combine(Path.GetTempPath(), "EquipmentTrackingTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(Path.Combine(folder, "data"));
            var logger = new FileLogger(paths);
            var backups = new BackupService(paths, logger);
            var settings = new SettingsService(paths, logger);
            var records = Path.Combine(folder, "records");
            var scheduledFolder = Path.Combine(folder, "scheduled");
            Directory.CreateDirectory(records);
            await File.WriteAllTextAsync(Path.Combine(records, "record.pdf"), "monday-morning");
            await settings.SaveAsync(new AppSettings
            {
                CompletedPdfFolder = records,
                ScheduledBackupFolder = scheduledFolder,
                ScheduledBackupsEnabled = true,
                BackupRetentionCount = 5
            });
            var database = new DatabaseService(paths, logger, backups);
            await database.InitializeAsync(createAutomaticBackup: false);
            using var scheduler = new ScheduledBackupService(
                paths,
                settings,
                database,
                backups,
                new StatusService(),
                logger);

            var offset = TimeSpan.FromHours(-6);
            await scheduler.RunDueBackupsAsync(new DateTimeOffset(2026, 8, 10, 9, 0, 0, offset));
            Assert.Single(Directory.GetFiles(scheduledFolder, "*Full-*.zip"));

            await File.WriteAllTextAsync(Path.Combine(records, "record.pdf"), "monday-afternoon");
            await scheduler.RunDueBackupsAsync(new DateTimeOffset(2026, 8, 10, 16, 0, 0, offset));
            Assert.Single(Directory.GetFiles(scheduledFolder, "*Differential-*.zip"));

            await File.WriteAllTextAsync(Path.Combine(records, "record.pdf"), "tuesday-afternoon");
            await scheduler.RunDueBackupsAsync(new DateTimeOffset(2026, 8, 11, 16, 0, 0, offset));
            Assert.Equal(2, Directory.GetFiles(scheduledFolder, "*Differential-*.zip").Length);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task MondayBeforeFullTimeCatchesUpThePriorFridayDifferential()
    {
        SQLitePCL.Batteries_V2.Init();
        var folder = Path.Combine(Path.GetTempPath(), "EquipmentTrackingTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(Path.Combine(folder, "data"));
            var logger = new FileLogger(paths);
            var backups = new BackupService(paths, logger);
            var settings = new SettingsService(paths, logger);
            var records = Path.Combine(folder, "records");
            var scheduledFolder = Path.Combine(folder, "scheduled");
            Directory.CreateDirectory(records);
            await File.WriteAllTextAsync(Path.Combine(records, "record.pdf"), "monday");
            await settings.SaveAsync(new AppSettings
            {
                CompletedPdfFolder = records,
                ScheduledBackupFolder = scheduledFolder,
                ScheduledBackupsEnabled = true,
                BackupRetentionCount = 5
            });
            var database = new DatabaseService(paths, logger, backups);
            await database.InitializeAsync(createAutomaticBackup: false);
            using var scheduler = new ScheduledBackupService(
                paths,
                settings,
                database,
                backups,
                new StatusService(),
                logger);

            var offset = TimeSpan.FromHours(-6);
            await scheduler.RunDueBackupsAsync(
                new DateTimeOffset(2026, 8, 3, 9, 0, 0, offset));
            await File.WriteAllTextAsync(Path.Combine(records, "record.pdf"), "friday-change");

            await scheduler.RunDueBackupsAsync(
                new DateTimeOffset(2026, 8, 10, 8, 0, 0, offset));

            Assert.Single(Directory.GetFiles(scheduledFolder, "*Differential-*.zip"));
            Assert.Single(Directory.GetFiles(scheduledFolder, "*Full-*.zip"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
