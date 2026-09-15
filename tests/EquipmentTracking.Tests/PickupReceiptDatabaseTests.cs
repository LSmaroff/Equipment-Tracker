using System.Security.Cryptography;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;
using Microsoft.Data.Sqlite;

namespace EquipmentTracking.Tests;

public sealed class PickupReceiptDatabaseTests
{
    [Fact]
    public async Task CommitPickupReceiptReturnsOnlySelectedDevicesAndIsIdempotent()
    {
        SQLitePCL.Batteries_V2.Init();
        var folder = CreateTemporaryFolder();
        try
        {
            var (database, paths) = await CreateDatabaseAsync(folder);
            var parentPdf = await WriteSyntheticPdfAsync(folder, "parent.pdf", "synthetic parent");
            var receiptPdf = await WriteSyntheticPdfAsync(folder, "pickup-1.pdf", "synthetic signed pickup one");
            var transaction = BuildTransaction("TX-PICKUP-SUBSET", parentPdf, 3);
            Assert.True(await database.InsertTransactionAsync(transaction));
            var pickedUpAt = new DateTimeOffset(2026, 9, 15, 8, 30, 0, TimeSpan.FromHours(-6));
            var receipt = BuildReceipt(
                "pickup-subset-1",
                transaction.Id,
                sequenceNumber: 1,
                receiptPdf,
                pickedUpAt,
                [transaction.Devices[2].Id, transaction.Devices[0].Id]);

            var result = await database.CommitPickupReceiptAsync(receipt);

            Assert.False(result.WasAlreadyCommitted);
            Assert.False(result.ParentArchived);
            Assert.Equal(2, result.PickedUpCount);
            Assert.Equal(1, result.RemainingDeviceCount);

            var reloaded = await database.GetTransactionByIdAsync(transaction.Id);
            Assert.NotNull(reloaded);
            Assert.False(reloaded.IsArchived);
            Assert.Equal(parentPdf, reloaded.PdfPath);
            Assert.Equal(DeviceStatusCatalog.Returned, reloaded.Devices[0].Status);
            Assert.Equal(DeviceStatusCatalog.InShop, reloaded.Devices[1].Status);
            Assert.Equal(DeviceStatusCatalog.Returned, reloaded.Devices[2].Status);
            Assert.Equal(pickedUpAt, reloaded.Devices[0].ReturnedAt);
            Assert.Equal(pickedUpAt, reloaded.Devices[2].ReturnedAt);

            var stored = Assert.Single(await database.GetPickupReceiptsAsync(transaction.Id));
            Assert.Equal(receipt.Id, stored.Id);
            Assert.Equal(
                [transaction.Devices[0].Id, transaction.Devices[2].Id],
                stored.DeviceIds);
            Assert.Equal(receiptPdf, stored.PdfPath);
            Assert.Equal(receipt.Sha256, stored.Sha256);
            Assert.Single(
                await database.GetFileArtifactsAsync(transaction.Id),
                item => item.ArtifactType == DatabaseService.SignedPartialPickupArtifactType);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.UpdateDeviceStatusAsync(
                    transaction.Devices[0].Id,
                    DeviceStatusCatalog.InShop,
                    "SSgt Synthetic",
                    "Must not reactivate a returned device."));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.UpdateDeviceStatusAsync(
                    transaction.Devices[1].Id,
                    DeviceStatusCatalog.Returned,
                    "SSgt Synthetic",
                    "Must use a signed pickup workflow."));
            var afterRejectedUpdates = await database.GetTransactionByIdAsync(transaction.Id);
            Assert.NotNull(afterRejectedUpdates);
            Assert.Equal(DeviceStatusCatalog.Returned, afterRejectedUpdates.Devices[0].Status);
            Assert.Equal(DeviceStatusCatalog.InShop, afterRejectedUpdates.Devices[1].Status);

            await using (var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = paths.DatabasePath,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false
                }.ToString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT DeviceNumber FROM PartialPickupDevices WHERE PartialPickupId=$id ORDER BY DeviceNumber;";
                command.Parameters.AddWithValue("$id", receipt.Id);
                var numbers = new List<int>();
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    numbers.Add(reader.GetInt32(0));
                }
                Assert.Equal([1, 3], numbers);
            }

            var auditCount = (await database.GetSnapshotAsync()).AuditRecords.Count;
            var replay = await database.CommitPickupReceiptAsync(receipt);
            Assert.True(replay.WasAlreadyCommitted);
            Assert.Equal(auditCount, (await database.GetSnapshotAsync()).AuditRecords.Count);
            Assert.Single(await database.GetPickupReceiptsAsync(transaction.Id));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryFolder(folder);
        }
    }

    [Fact]
    public async Task FinalSignedPickupReceiptArchivesParentWithoutChangingEarlierReturnTime()
    {
        SQLitePCL.Batteries_V2.Init();
        var folder = CreateTemporaryFolder();
        try
        {
            var (database, _) = await CreateDatabaseAsync(folder);
            var parentPdf = await WriteSyntheticPdfAsync(folder, "parent.pdf", "synthetic parent");
            var firstPdf = await WriteSyntheticPdfAsync(folder, "pickup-1.pdf", "synthetic signed pickup one");
            var finalPdf = await WriteSyntheticPdfAsync(folder, "pickup-2.pdf", "synthetic signed pickup two");
            var transaction = BuildTransaction("TX-PICKUP-FINAL", parentPdf, 2);
            Assert.True(await database.InsertTransactionAsync(transaction));
            var firstTime = new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.FromHours(-6));
            var finalTime = firstTime.AddHours(2);

            await database.CommitPickupReceiptAsync(BuildReceipt(
                "pickup-final-1",
                transaction.Id,
                sequenceNumber: 1,
                firstPdf,
                firstTime,
                [transaction.Devices[0].Id]));
            Assert.Equal(2, await database.GetNextPickupSequenceAsync(transaction.Id));

            var finalResult = await database.CommitPickupReceiptAsync(BuildReceipt(
                "pickup-final-2",
                transaction.Id,
                sequenceNumber: 2,
                finalPdf,
                finalTime,
                [transaction.Devices[1].Id]));

            Assert.True(finalResult.ParentArchived);
            Assert.Equal(0, finalResult.RemainingDeviceCount);
            var reloaded = await database.GetTransactionByIdAsync(transaction.Id);
            Assert.NotNull(reloaded);
            Assert.True(reloaded.IsArchived);
            Assert.Equal(finalPdf, reloaded.PdfPath);
            Assert.Equal(finalTime, reloaded.ClosedAt);
            Assert.Equal("SSgt Synthetic", reloaded.CloseoutTechnician);
            Assert.Equal(firstTime, reloaded.Devices[0].ReturnedAt);
            Assert.Equal(finalTime, reloaded.Devices[1].ReturnedAt);
            Assert.Equal(2, (await database.GetPickupReceiptsAsync(transaction.Id)).Count);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => database.GetNextPickupSequenceAsync(transaction.Id));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryFolder(folder);
        }
    }

    [Fact]
    public async Task InvalidMixedTransactionSelectionRollsBackReceiptArtifactAndStatuses()
    {
        SQLitePCL.Batteries_V2.Init();
        var folder = CreateTemporaryFolder();
        try
        {
            var (database, _) = await CreateDatabaseAsync(folder);
            var firstParent = await WriteSyntheticPdfAsync(folder, "parent-1.pdf", "synthetic parent one");
            var secondParent = await WriteSyntheticPdfAsync(folder, "parent-2.pdf", "synthetic parent two");
            var receiptPdf = await WriteSyntheticPdfAsync(folder, "pickup.pdf", "synthetic signed pickup");
            var first = BuildTransaction("TX-PICKUP-ONE", firstParent, 1);
            var second = BuildTransaction("TX-PICKUP-TWO", secondParent, 1);
            Assert.True(await database.InsertTransactionAsync(first));
            Assert.True(await database.InsertTransactionAsync(second));
            var receipt = BuildReceipt(
                "pickup-invalid-mixed",
                first.Id,
                sequenceNumber: 1,
                receiptPdf,
                DateTimeOffset.Now,
                [first.Devices[0].Id, second.Devices[0].Id]);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => database.CommitPickupReceiptAsync(receipt));

            Assert.Empty(await database.GetPickupReceiptsAsync(first.Id));
            Assert.DoesNotContain(
                await database.GetFileArtifactsAsync(first.Id),
                item => item.ArtifactType == DatabaseService.SignedPartialPickupArtifactType);
            Assert.Equal(
                DeviceStatusCatalog.InShop,
                (await database.GetTransactionByIdAsync(first.Id))!.Devices[0].Status);
            Assert.Equal(
                DeviceStatusCatalog.InShop,
                (await database.GetTransactionByIdAsync(second.Id))!.Devices[0].Status);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryFolder(folder);
        }
    }

    [Fact]
    public async Task CloseoutPreparationBlocksPickupUntilSafelyCleared()
    {
        SQLitePCL.Batteries_V2.Init();
        var folder = CreateTemporaryFolder();
        try
        {
            var (database, _) = await CreateDatabaseAsync(folder);
            var parentPdf = await WriteSyntheticPdfAsync(folder, "parent.pdf", "synthetic parent");
            var receiptPdf = await WriteSyntheticPdfAsync(folder, "pickup.pdf", "synthetic signed pickup");
            var transaction = BuildTransaction("TX-PICKUP-CLOSEOUT", parentPdf, 1);
            Assert.True(await database.InsertTransactionAsync(transaction));
            await database.MarkCloseoutPreparedAsync(transaction.Id, "SSgt Synthetic");
            var receipt = BuildReceipt(
                "pickup-after-closeout",
                transaction.Id,
                sequenceNumber: 1,
                receiptPdf,
                DateTimeOffset.Now,
                [transaction.Devices[0].Id]);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => database.GetNextPickupSequenceAsync(transaction.Id));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => database.CommitPickupReceiptAsync(receipt));

            await database.ClearCloseoutPreparedAsync(transaction.Id);

            Assert.Equal(1, await database.GetNextPickupSequenceAsync(transaction.Id));
            Assert.Null((await database.GetTransactionByIdAsync(transaction.Id))!.CloseoutPreparedAt);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryFolder(folder);
        }
    }

    private static async Task<(DatabaseService Database, AppPaths Paths)> CreateDatabaseAsync(
        string folder)
    {
        var paths = new AppPaths(Path.Combine(folder, "data"));
        var logger = new FileLogger(paths);
        var backups = new BackupService(paths, logger);
        var database = new DatabaseService(paths, logger, backups);
        await database.InitializeAsync(createAutomaticBackup: false);
        return (database, paths);
    }

    private static EquipmentTransaction BuildTransaction(
        string id,
        string parentPdf,
        int deviceCount) => new()
    {
        Id = id,
        Customer = new CustomerIdentity
        {
            Rank = "A1C",
            FirstName = "Synthetic",
            LastName = "Customer",
            OriginalName = "SYNTHETIC.CUSTOMER"
        },
        PhoneNumber = "555-0100",
        Technician = "SSgt Synthetic",
        Organization = "TEST ORG",
        TicketNumber = id,
        IssuedAt = DateTimeOffset.Now,
        CreatedAt = DateTimeOffset.Now,
        PdfPath = parentPdf,
        PdfSignerName = "Synthetic Customer",
        Devices = Enumerable.Range(1, deviceCount)
            .Select(index => new DeviceRecord
            {
                PartNumber = $"PART-{index}",
                Model = $"MODEL-{index}",
                SerialNumber = $"{id}-SERIAL-{index}",
                AssetTag = $"ASSET-{index}",
                Status = DeviceStatusCatalog.InShop
            })
            .ToList()
    };

    private static PickupReceipt BuildReceipt(
        string id,
        string transactionId,
        int sequenceNumber,
        string pdfPath,
        DateTimeOffset pickedUpAt,
        IReadOnlyList<long> deviceIds)
    {
        var bytes = File.ReadAllBytes(pdfPath);
        return new PickupReceipt
        {
            Id = id,
            TransactionId = transactionId,
            SequenceNumber = sequenceNumber,
            PdfPath = pdfPath,
            Technician = "SSgt Synthetic",
            Notes = "Synthetic partial pickup test.",
            PickedUpAt = pickedUpAt,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            SizeBytes = bytes.LongLength,
            SignerName = "Synthetic Customer",
            CertificateSubject = "CN=Synthetic Customer",
            CertificateThumbprint = "00112233445566778899AABBCCDDEEFF00112233",
            SignatureTime = pickedUpAt,
            CreatedAt = pickedUpAt,
            DeviceIds = deviceIds
        };
    }

    private static async Task<string> WriteSyntheticPdfAsync(
        string folder,
        string fileName,
        string contents)
    {
        var path = Path.GetFullPath(Path.Combine(folder, "records", fileName));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, contents);
        return path;
    }

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
            // Test cleanup must not hide the test result.
        }
    }
}
