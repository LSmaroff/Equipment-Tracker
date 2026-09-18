using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;
using EquipmentTracking.App.ViewModels;
using Microsoft.Data.Sqlite;

namespace EquipmentTracking.Tests;

public sealed class DashboardDataIntegrationTests
{
    private const string ActiveTransactionId = "TX-20260810-143025-A1B2C3D4";
    private const string ArchivedTransactionId = "TX-20260810-143026-E5F6A7B8";

    [Fact]
    public async Task DashboardQueriesRespectScopeAndExposeActiveDeviceData()
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

            var active = BuildActiveTransaction();
            var archived = BuildArchivedTransaction();
            Assert.True(await database.InsertTransactionAsync(active));
            Assert.True(await database.InsertTransactionAsync(archived));

            var archivedPdfPath = Path.Combine(folder, "archive", "archived-1297.pdf");
            var closedAt = DateTimeOffset.Now.AddMinutes(-1);
            await database.ArchiveTransactionAsync(
                archived.Id,
                archivedPdfPath,
                "TSgt Closer",
                closedAt);

            var summary = await database.GetDashboardSummaryAsync();
            Assert.Equal(1, summary.ActiveDeviceCount);
            Assert.Equal(1, summary.ActiveTransactionCount);
            Assert.Equal(1, summary.ArchivedTransactionCount);

            var activeRows = await database.SearchTransactionsAsync(string.Empty, archivedOnly: false);
            var activeRow = Assert.Single(activeRows);
            Assert.Equal(ActiveTransactionId, activeRow.TransactionId);
            Assert.Equal("MSgt Doe, Jane Q", activeRow.CustomerName);
            Assert.False(activeRow.IsArchived);
            Assert.Equal(2, activeRow.DeviceCount);
            Assert.Equal(
                new[] { DeviceStatusCatalog.InShop, DeviceStatusCatalog.Returned }.Order(),
                activeRow.StatusSummary.Split(',', StringSplitOptions.RemoveEmptyEntries).Order());

            foreach (var deviceFieldQuery in new[]
                     {
                         "HP-8A3",
                         "EliteBook 830 G8",
                         "SN-ACTIVE-001",
                         "ASSET-ACTIVE-001",
                         DeviceStatusCatalog.InShop
                     })
            {
                var matches = await database.SearchTransactionsAsync(
                    deviceFieldQuery,
                    archivedOnly: false);
                Assert.Equal(ActiveTransactionId, Assert.Single(matches).TransactionId);
            }

            Assert.Empty(await database.SearchTransactionsAsync("ARCH-PART", archivedOnly: false));
            var archivedRows = await database.SearchTransactionsAsync("ARCH-PART", archivedOnly: true);
            var archivedRow = Assert.Single(archivedRows);
            Assert.Equal(ArchivedTransactionId, archivedRow.TransactionId);
            Assert.True(archivedRow.IsArchived);
            Assert.Equal(closedAt, archivedRow.ClosedAt);
            Assert.Equal(archivedPdfPath, archivedRow.PdfPath);
            Assert.Equal(1, archivedRow.DeviceCount);
            Assert.Equal(DeviceStatusCatalog.Returned, archivedRow.StatusSummary);

            var devices = await database.SearchDevicesAsync("EliteBook 830 G8");
            var device = Assert.Single(devices);
            Assert.Equal(ActiveTransactionId, device.TransactionId);
            Assert.Equal("HP-8A3", device.PartNumber);
            Assert.Equal("EliteBook 830 G8", device.Model);
            Assert.Equal("SN-ACTIVE-001", device.SerialNumber);
            Assert.Equal("ASSET-ACTIVE-001", device.AssetTag);
            Assert.Equal("MSgt Doe, Jane Q", device.CustomerName);

            var modelChart = await database.GetDeviceChartDataAsync("Model");
            var modelPoint = Assert.Single(modelChart);
            Assert.Equal("EliteBook 830 G8", modelPoint.Label);
            Assert.Equal(1, modelPoint.Value);

            var statusChart = await database.GetDeviceChartDataAsync("Status");
            var statusPoint = Assert.Single(statusChart);
            Assert.Equal(DeviceStatusCatalog.InShop, statusPoint.Label);
            Assert.Equal(1, statusPoint.Value);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryFolder(folder);
        }
    }

    [Fact]
    public async Task ExactRecordCodeSearchSwitchesDashboardBetweenArchiveAndActiveScopes()
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

            var active = BuildActiveTransaction();
            var archived = BuildArchivedTransaction();
            Assert.True(await database.InsertTransactionAsync(active));
            Assert.True(await database.InsertTransactionAsync(archived));
            await database.ArchiveTransactionAsync(
                archived.Id,
                Path.Combine(folder, "archive", "archived-1297.pdf"),
                "TSgt Closer",
                DateTimeOffset.Now.AddMinutes(-1));

            var settings = new SettingsService(paths, logger);
            var pdf = new PdfFormService(logger);
            var signature = new SignatureExtractionService(new CertificateNameParser(), logger);
            var excel = new ExcelExportService(database, settings, logger);
            using var journals = new WorkflowJournalService(paths, logger);
            var workflow = new TransactionWorkflowService(
                paths,
                settings,
                pdf,
                signature,
                database,
                excel,
                new FileNameService(),
                journals,
                logger);
            var recordCodes = new RecordCodeService();
            var viewModel = new DashboardViewModel(
                database,
                workflow,
                new AdobeService(settings, logger),
                new PrintJobService(paths, logger),
                recordCodes,
                new StatusService(),
                logger,
                new TransactionDocumentService(database, settings, new PrintJobService(paths, logger)));

            viewModel.SearchQuery = recordCodes.BuildPayload(ArchivedTransactionId);
            await viewModel.SearchAsync();

            Assert.Equal(ArchivedTransactionId, viewModel.SearchQuery);
            Assert.Equal("Archive", viewModel.SelectedTransactionScope);
            Assert.True(viewModel.IsArchiveView);
            Assert.Equal(ArchivedTransactionId, Assert.Single(viewModel.Transactions).TransactionId);
            Assert.Equal(ArchivedTransactionId, viewModel.SelectedTransaction?.TransactionId);

            viewModel.SearchQuery = recordCodes.BuildPayload(ActiveTransactionId);
            await viewModel.SearchAsync();

            Assert.Equal(ActiveTransactionId, viewModel.SearchQuery);
            Assert.Equal("Active 1297s", viewModel.SelectedTransactionScope);
            Assert.False(viewModel.IsArchiveView);
            Assert.Equal(ActiveTransactionId, Assert.Single(viewModel.Transactions).TransactionId);
            Assert.Equal(ActiveTransactionId, viewModel.SelectedTransaction?.TransactionId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryFolder(folder);
        }
    }

    private static EquipmentTransaction BuildActiveTransaction() => new()
    {
        Id = ActiveTransactionId,
        Customer = new CustomerIdentity
        {
            Rank = "MSgt",
            FirstName = "Jane",
            MiddleInitial = "Q",
            LastName = "Doe",
            OriginalName = "DOE.JANE.Q"
        },
        PhoneNumber = "555-0100",
        Technician = "SSgt Technician",
        Organization = "58 SOW",
        TicketNumber = "TICKET-ACTIVE",
        IssuedAt = DateTimeOffset.Now.AddHours(-2),
        CreatedAt = DateTimeOffset.Now.AddHours(-2),
        PdfPath = Path.Combine(Path.GetTempPath(), "active-1297.pdf"),
        PdfSignerName = "Signer, Customer",
        Devices =
        [
            new DeviceRecord
            {
                PartNumber = "HP-8A3",
                Model = "EliteBook 830 G8",
                SerialNumber = "SN-ACTIVE-001",
                AssetTag = "ASSET-ACTIVE-001",
                Status = DeviceStatusCatalog.InShop
            },
            new DeviceRecord
            {
                PartNumber = "DOCK-PART",
                Model = "Dock Station",
                SerialNumber = "SN-ACTIVE-002",
                AssetTag = "ASSET-ACTIVE-002",
                Status = DeviceStatusCatalog.Returned,
                ReturnedAt = DateTimeOffset.Now
            }
        ]
    };

    private static EquipmentTransaction BuildArchivedTransaction() => new()
    {
        Id = ArchivedTransactionId,
        Customer = new CustomerIdentity
        {
            Rank = "Capt",
            FirstName = "Alex",
            LastName = "Smith",
            OriginalName = "SMITH.ALEX"
        },
        PhoneNumber = "555-0200",
        Technician = "SSgt Technician",
        Organization = "58 SOW",
        TicketNumber = "TICKET-ARCHIVED",
        IssuedAt = DateTimeOffset.Now.AddHours(-3),
        CreatedAt = DateTimeOffset.Now.AddHours(-3),
        PdfPath = Path.Combine(Path.GetTempPath(), "before-archive.pdf"),
        PdfSignerName = "Archived, Signer",
        Devices =
        [
            new DeviceRecord
            {
                PartNumber = "ARCH-PART",
                Model = "Archived Model",
                SerialNumber = "SN-ARCHIVED-001",
                AssetTag = "ASSET-ARCHIVED-001",
                Status = DeviceStatusCatalog.ReadyForPickup
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
            // Cleanup must not hide the test result.
        }
    }
}
