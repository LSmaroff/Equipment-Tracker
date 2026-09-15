using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;
using Microsoft.Data.Sqlite;

namespace EquipmentTracking.Tests;

public sealed class DeviceRecognitionDatabaseTests
{
    [Fact]
    public async Task ExactSerialHistoryReturnsOnlyMatchingStoredIdentity()
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

            Assert.True(await database.InsertTransactionAsync(
                BuildTransaction("TX-HISTORY-EXACT", "2MQ5390WTS", "A4TH1AV")));
            Assert.True(await database.InsertTransactionAsync(
                BuildTransaction("TX-HISTORY-NEAR", "2MQ5390WTX", "WRONG-PART")));

            var history = await database.GetDeviceIdentityHistoryAsync(" 2mq5390wts ");

            var item = Assert.Single(history);
            Assert.Equal("A4TH1AV", item.PartNumber);
            Assert.Equal("HP EliteBook 645", item.ModelName);
            Assert.Equal(">«RS»06«GS»18S7ESQ72MQ5390WTS", item.RawScanValue);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryFolder(folder);
        }
    }

    private static EquipmentTransaction BuildTransaction(
        string id,
        string serialNumber,
        string partNumber) => new()
    {
        Id = id,
        Customer = new CustomerIdentity
        {
            Rank = "A1C",
            FirstName = "Synthetic",
            LastName = "Tester"
        },
        PhoneNumber = "555-0100",
        Technician = "SSgt Tester",
        Organization = "Test Organization",
        TicketNumber = id,
        IssuedAt = DateTimeOffset.Now,
        CreatedAt = DateTimeOffset.Now,
        PdfPath = Path.Combine(Path.GetTempPath(), id + ".pdf"),
        Devices =
        [
            new DeviceRecord
            {
                PartNumber = partNumber,
                Model = partNumber == "A4TH1AV" ? "HP EliteBook 645" : "Wrong Model",
                SerialNumber = serialNumber,
                RawScanValue = $">«RS»06«GS»18S7ESQ7{serialNumber}",
                Status = DeviceStatusCatalog.Returned
            }
        ]
    };

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
            // Test cleanup should not hide the test result.
        }
    }
}
