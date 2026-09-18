using System.Security.Cryptography;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;
using Microsoft.Data.Sqlite;
using PdfSharp.Pdf.IO;

namespace EquipmentTracking.Tests;

public sealed class TransactionDocumentServiceTests
{
    [Fact]
    public async Task CurrentCopiesIncludeEveryCommittedPickup_ReceiptsKeepTheirOwnSubset_AndSourcesAreUnchanged()
    {
        using var fixture = await Fixture.CreateAsync();
        var sourceHash = Hash(fixture.Parent.PdfPath);
        Assert.Equal(fixture.Parent.PdfPath,
            await fixture.Documents.CreateCurrentCopyAsync(fixture.Parent.Id, forPrinting: false));

        var first = await fixture.PickupAsync(1, 1);
        var firstHash = Hash(first.PdfPath);
        var current = await fixture.Documents.CreateCurrentCopyAsync(fixture.Parent.Id, forPrinting: false);
        AssertCopy(current, "Device1", twoCopies: false);
        var firstPrint = await fixture.Documents.CreateCurrentCopyAsync(fixture.Parent.Id, forPrinting: true);
        AssertCopy(firstPrint, "Device1", twoCopies: true);

        var second = await fixture.PickupAsync(2, 3);
        var secondHash = Hash(second.PdfPath);
        var cumulative = await fixture.Documents.CreateCurrentCopyAsync(fixture.Parent.Id, forPrinting: true);
        AssertCopy(cumulative, "Device1,Device3", twoCopies: true);
        var receiptPrint = await fixture.Documents.CreatePreservedCopyAsync(fixture.Parent.Id, second.PdfPath, forPrinting: true);
        AssertCopy(receiptPrint, "Device3", twoCopies: true);
        var receiptView = await fixture.Documents.CreatePreservedCopyAsync(fixture.Parent.Id, second.PdfPath, forPrinting: false);
        AssertCopy(receiptView, "Device3", twoCopies: false);
        AssertCopy(await fixture.Documents.CreatePreservedCopyAsync(fixture.Parent.Id, first.PdfPath, true), "Device1", true);

        var originalPrint = await fixture.Documents.CreatePreservedCopyAsync(fixture.Parent.Id, fixture.Parent.PdfPath, true);
        AssertCopy(originalPrint, "", true);
        Assert.Equal(fixture.Parent.PdfPath,
            await fixture.Documents.CreatePreservedCopyAsync(fixture.Parent.Id, fixture.Parent.PdfPath, false));
        var reloaded = await fixture.Database.GetTransactionByIdAsync(fixture.Parent.Id);
        Assert.NotNull(reloaded);
        Assert.False(reloaded.IsArchived);
        Assert.Equal(fixture.Parent.PdfPath, reloaded.PdfPath);
        Assert.Equal(DeviceStatusCatalog.InShop, reloaded.Devices[1].Status);

        var final = await fixture.PickupAsync(3, 2);
        var finalHash = Hash(final.PdfPath);
        var archivedView = await fixture.Documents.CreateCurrentCopyAsync(fixture.Parent.Id, false);
        AssertCopy(archivedView, "Device1,Device2,Device3", false);
        var archivedPrint = await fixture.Documents.CreateCurrentCopyAsync(fixture.Parent.Id, true);
        AssertCopy(archivedPrint, "Device1,Device2,Device3", true);
        AssertCopy(await fixture.Documents.CreatePreservedCopyAsync(fixture.Parent.Id, final.PdfPath, true), "Device2", true);
        Assert.Equal(sourceHash, Hash(fixture.Parent.PdfPath));
        Assert.Equal(firstHash, Hash(first.PdfPath));
        Assert.Equal(secondHash, Hash(second.PdfPath));
        Assert.Equal(finalHash, Hash(final.PdfPath));
        reloaded = await fixture.Database.GetTransactionByIdAsync(fixture.Parent.Id);
        Assert.NotNull(reloaded);
        Assert.True(reloaded.IsArchived);
        Assert.Equal(final.PdfPath, reloaded.PdfPath);

        var qa = Environment.GetEnvironmentVariable("ETP_DOCUMENT_QA_OUTPUT");
        if (!string.IsNullOrWhiteSpace(qa))
        {
            Directory.CreateDirectory(qa);
            foreach (var (path, name) in new[]
            {
                (current, "current-status.pdf"), (cumulative, "cumulative-print.pdf"),
                (receiptView, "pickup-view.pdf"), (receiptPrint, "pickup-print.pdf"),
                (originalPrint, "original-print.pdf"), (archivedView, "archived-view.pdf"),
                (archivedPrint, "archived-print.pdf")
            }) File.Copy(path, Path.Combine(qa, name), overwrite: true);
        }
        Assert.True(await fixture.PrintJobs.DeleteTemporaryPrintJobAsync(current));
        Assert.True(await fixture.PrintJobs.DeleteTemporaryPrintJobAsync(receiptPrint));
        Assert.False(File.Exists(current));
        Assert.True(File.Exists(first.PdfPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrIncorrectDeviceMappingFailsWithoutChangingEvidence(bool missing)
    {
        using var fixture = await Fixture.CreateAsync();
        var receipt = await fixture.PickupAsync(1, 1);
        var hash = Hash(receipt.PdfPath);
        if (missing) fixture.Settings.Current.PdfFieldMappings.Remove("Device1");
        else fixture.Settings.Current.PdfFieldMappings["Device1"] = "NonexistentDevice";
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Documents.CreateCurrentCopyAsync(fixture.Parent.Id, true));
        Assert.Equal(hash, Hash(receipt.PdfPath));
        Assert.Empty(Directory.GetFiles(fixture.Paths.PrintJobsDirectory, "*.pdf"));
    }

    [Fact]
    public async Task UnrelatedDocumentCannotBePresentedAsAReceipt()
    {
        using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Documents.CreatePreservedCopyAsync(
            fixture.Parent.Id, Path.Combine(fixture.Folder, "unrelated.pdf"), true));
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void AssertCopy(string path, string fields, bool twoCopies)
    {
        using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
        var page = Assert.Single(pdf.Pages.Cast<PdfSharp.Pdf.PdfPage>());
        Assert.Equal(fields, page.Elements.GetString("/ETPReturnedFields"));
        Assert.Equal(twoCopies ? 792d : 378d, page.EffectiveCropBoxReadOnly.Height, 1);
        Assert.Null(pdf.Internals.Catalog.Elements["/AcroForm"]);
        Assert.Contains("Derived reference copy", pdf.Info.Subject);
        Assert.DoesNotContain(page.Annotations.Cast<PdfSharp.Pdf.Annotations.PdfAnnotation>(),
            annotation => annotation.Elements.GetName("/Subtype") == "/Widget");
        // All three signature appearances remain available on each displayed copy.
        Assert.Equal(twoCopies ? 6 : 3, page.Annotations.Count);
        Assert.Contains("Arial", pdf.Internals.Catalog.ToString() + string.Join(" ",
            pdf.Internals.GetAllObjects().Select(item => item.ToString())));
    }

    private sealed class Fixture : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "EquipmentTrackingTests", Guid.NewGuid().ToString("N"));
        public AppPaths Paths { get; private set; } = null!;
        public DatabaseService Database { get; private set; } = null!;
        public SettingsService Settings { get; private set; } = null!;
        public PrintJobService PrintJobs { get; private set; } = null!;
        public TransactionDocumentService Documents { get; private set; } = null!;
        public EquipmentTransaction Parent { get; private set; } = null!;
        private PdfFormService Forms { get; set; } = null!;

        public static async Task<Fixture> CreateAsync()
        {
            SQLitePCL.Batteries_V2.Init();
            var result = new Fixture();
            Directory.CreateDirectory(result.Folder);
            result.Paths = new AppPaths(Path.Combine(result.Folder, "data"));
            var logger = new FileLogger(result.Paths);
            result.Settings = new SettingsService(result.Paths, logger);
            result.Database = new DatabaseService(result.Paths, logger, new BackupService(result.Paths, logger));
            await result.Database.InitializeAsync(createAutomaticBackup: false);
            result.PrintJobs = new PrintJobService(result.Paths, logger);
            result.Documents = new TransactionDocumentService(result.Database, result.Settings, result.PrintJobs);
            result.Forms = new PdfFormService(logger);
            var original = Path.Combine(result.Folder, "synthetic-intake.pdf");
            result.Parent = new EquipmentTransaction
            {
                Id = "TX-SYNTHETIC-PRINT", TicketNumber = "SYNTHETIC-PRINT", PdfPath = original,
                IssuedAt = DateTimeOffset.Now, CreatedAt = DateTimeOffset.Now,
                Customer = new CustomerIdentity { Rank = "MSgt", FirstName = "Synthetic", LastName = "Customer" },
                Devices = Enumerable.Range(1, 3).Select(number => new DeviceRecord
                {
                    Model = "HP EliteBook 645", PartNumber = $"SYN-PART-{number}", SerialNumber = $"SYN-SERIAL-{number}",
                    Status = DeviceStatusCatalog.InShop
                }).ToList()
            };
            var values = new Dictionary<string, string>
            {
                ["TechnicianNameGrade"] = "MSgt Synthetic Customer", ["TicketNumber"] = result.Parent.TicketNumber,
                ["PhoneNumber"] = "555-0100", ["Organization"] = "SYNTHETIC TEST UNIT", ["Quantity"] = "3",
                ["IssueDate"] = "09/17/2026", ["ReturnDate"] = ""
            };
            for (var index = 0; index < 3; index++)
                values[$"Device{index + 1}"] = $"Model Name: HP EliteBook 645\nPart Number: SYN-PART-{index + 1}\nSerial Number: SYN-SERIAL-{index + 1}";
            result.Forms.FillFields(Path.Combine(AppContext.BaseDirectory, "Templates", "1297-58SOW-SC-TEMPLATE.pdf"),
                original, values, result.Settings.Current, "TX-20260917-120000-1234ABCD");
            await result.Database.InsertTransactionAsync(result.Parent);
            return result;
        }

        public async Task<PickupReceipt> PickupAsync(int sequence, int deviceNumber)
        {
            var path = Path.Combine(Folder, $"synthetic-pickup-{sequence}.pdf");
            Forms.CreatePartialPickupPdf(Parent.PdfPath, path, [$"Device{deviceNumber}"], DateTimeOffset.Now, Settings.Current);
            // Synthetic persistence fixture only; production signature verification is covered by workflow tests.
            var receipt = new PickupReceipt
            {
                Id = $"synthetic-pickup-{sequence}", TransactionId = Parent.Id, SequenceNumber = sequence,
                PdfPath = path, DeviceIds = [Parent.Devices[deviceNumber - 1].Id],
                Sha256 = Hash(path), SizeBytes = new FileInfo(path).Length,
                Technician = "Synthetic Technician", SignerName = "Synthetic Customer", SignatureTime = DateTimeOffset.Now
            };
            await Database.CommitPickupReceiptAsync(receipt);
            return receipt;
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(Folder, recursive: true);
        }
    }
}
