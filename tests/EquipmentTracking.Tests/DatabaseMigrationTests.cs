using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;
using Microsoft.Data.Sqlite;

namespace EquipmentTracking.Tests;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public async Task InitializeCreatesCurrentSchemaAndPassesIntegrityCheck()
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

            Assert.Equal(DatabaseService.CurrentSchemaVersion, await database.GetCurrentSchemaVersionAsync());
            Assert.Equal("ok", await database.VerifyIntegrityAsync());
            Assert.True(File.Exists(paths.DatabasePath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryFolder(folder);
        }
    }


    [Fact]
    public async Task LegacyDatabaseIsBackedUpAndMigratedWithoutLosingRecords()
    {
        SQLitePCL.Batteries_V2.Init();
        var folder = CreateTemporaryFolder();
        try
        {
            var paths = new AppPaths(folder);
            paths.EnsureDirectories();
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = paths.DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString();

            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    CREATE TABLE Transactions (
                        Id TEXT PRIMARY KEY,
                        CustomerFirstName TEXT NOT NULL,
                        CustomerMiddleInitial TEXT NOT NULL,
                        CustomerLastName TEXT NOT NULL,
                        CustomerOriginalName TEXT NOT NULL,
                        PhoneNumber TEXT NOT NULL,
                        Technician TEXT NOT NULL,
                        IssuedAt TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        PdfPath TEXT NOT NULL,
                        PdfSignerName TEXT NOT NULL,
                        CertificateSubject TEXT NOT NULL,
                        CertificateThumbprint TEXT NOT NULL,
                        SignatureTime TEXT NULL
                    );
                    CREATE TABLE Devices (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TransactionId TEXT NOT NULL,
                        Model TEXT NOT NULL,
                        SerialNumber TEXT NOT NULL,
                        AssetTag TEXT NOT NULL,
                        RawScanValue TEXT NOT NULL,
                        Status TEXT NOT NULL,
                        ReturnedAt TEXT NULL,
                        ReturnCondition TEXT NOT NULL DEFAULT '',
                        ReturnNotes TEXT NOT NULL DEFAULT ''
                    );
                    CREATE TABLE Audit (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TransactionId TEXT NOT NULL,
                        DeviceId INTEGER NULL,
                        Action TEXT NOT NULL,
                        Details TEXT NOT NULL,
                        Technician TEXT NOT NULL,
                        ComputerName TEXT NOT NULL,
                        ActionTime TEXT NOT NULL
                    );
                    CREATE TABLE Technicians (
                        NameGrade TEXT PRIMARY KEY COLLATE NOCASE,
                        LastUsedAt TEXT NOT NULL,
                        UseCount INTEGER NOT NULL DEFAULT 1
                    );
                    INSERT INTO Transactions VALUES (
                        'TX-LEGACY','Legacy','','User','LEGACY.USER','555-0100',
                        'SSgt Tester','2026-07-01T12:00:00+00:00','2026-07-01T12:00:00+00:00',
                        'C:\legacy.pdf','Legacy User','','',NULL
                    );
                    INSERT INTO Devices (
                        TransactionId,Model,SerialNumber,AssetTag,RawScanValue,Status,
                        ReturnedAt,ReturnCondition,ReturnNotes)
                    VALUES ('TX-LEGACY','MODEL-OLD','SERIAL-OLD','','','Issued',NULL,'','');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var logger = new FileLogger(paths);
            var backups = new BackupService(paths, logger);
            var database = new DatabaseService(paths, logger, backups);
            await database.InitializeAsync(createAutomaticBackup: true);

            Assert.Equal(DatabaseService.CurrentSchemaVersion, await database.GetCurrentSchemaVersionAsync());
            Assert.Equal("ok", await database.VerifyIntegrityAsync());
            Assert.NotEmpty(Directory.EnumerateFiles(paths.BackupDirectory, "EquipmentTracking-pre-migration-*.zip"));

            var transaction = await database.GetTransactionByIdAsync("TX-LEGACY");
            Assert.NotNull(transaction);
            Assert.Equal("Legacy", transaction.Customer.FirstName);
            Assert.Equal(string.Empty, transaction.Customer.Rank);
            Assert.Equal(string.Empty, transaction.Organization);
            Assert.False(transaction.IsArchived);
            Assert.Single(transaction.Devices);
            Assert.Equal(DeviceStatusCatalog.InShop, transaction.Devices[0].Status);
            Assert.Equal("MODEL-OLD", transaction.Devices[0].PartNumber);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryFolder(folder);
        }
    }

    [Fact]
    public async Task ActiveSerialNumberCannotBeInsertedTwiceButReturnedSerialCanBeReused()
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

            var first = BuildTransaction("TX-ONE", "SERIAL-001", DeviceStatusCatalog.InShop);
            var second = BuildTransaction("TX-TWO", " serial-001 ", DeviceStatusCatalog.InShop);
            Assert.True(await database.InsertTransactionAsync(first));
            await Assert.ThrowsAsync<SqliteException>(() => database.InsertTransactionAsync(second));

            await database.ArchiveTransactionAsync(
                first.Id,
                first.PdfPath,
                "SSgt Tester",
                DateTimeOffset.Now);

            Assert.True(await database.InsertTransactionAsync(second));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryFolder(folder);
        }
    }

    [Fact]
    public async Task ModelCatalogMappingIsReusedAndUpdatesMatchingDevices()
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

            var transaction = BuildTransaction("TX-MODEL", "SERIAL-MODEL", DeviceStatusCatalog.InShop);
            transaction.Devices[0].PartNumber = "PART-ABC";
            transaction.Devices[0].Model = string.Empty;
            Assert.True(await database.InsertTransactionAsync(transaction));

            await database.UpsertModelCatalogEntryAsync(
                "part-abc",
                "EliteBook 830 G8",
                "SSgt Tester",
                transaction.Id);

            Assert.Equal("EliteBook 830 G8", await database.ResolveModelNameAsync("PART-ABC"));
            var reloaded = await database.GetTransactionByIdAsync(transaction.Id);
            Assert.NotNull(reloaded);
            Assert.Equal("PART-ABC", reloaded.Devices[0].PartNumber);
            Assert.Equal("EliteBook 830 G8", reloaded.Devices[0].Model);
            Assert.Single(await database.GetModelCatalogAsync("EliteBook"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryFolder(folder);
        }
    }

    [Fact]
    public async Task FileArtifactRecordingIsIdempotent()
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

            await database.RecordFileArtifactAsync("TX-ONE", "OriginalSignedIntake", "C:\\test.pdf", "ABC", 123);
            await database.RecordFileArtifactAsync("TX-ONE", "OriginalSignedIntake", "C:\\test.pdf", "ABC", 123);

            var artifacts = await database.GetFileArtifactsAsync("TX-ONE");
            Assert.Single(artifacts);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryFolder(folder);
        }
    }

    private static EquipmentTransaction BuildTransaction(string id, string serial, string status) => new()
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
                PartNumber = "PART-1",
                Model = "MODEL-1",
                SerialNumber = serial,
                Status = status
            }
        ]
    };

    private static string CreateTemporaryFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "EquipmentTrackingTests", Guid.NewGuid().ToString("N"));
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
            // Test cleanup should not hide the test result.
        }
    }
}
