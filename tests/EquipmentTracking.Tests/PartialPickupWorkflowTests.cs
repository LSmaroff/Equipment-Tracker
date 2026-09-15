using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;
using Microsoft.Data.Sqlite;

namespace EquipmentTracking.Tests;

public sealed class PartialPickupWorkflowTests
{
    [Fact]
    public async Task UnsignedFinalizeThenSignedPreparedReceiptCanResume()
    {
        SQLitePCL.Batteries_V2.Init();
        await using var context = await WorkflowContext.CreateAsync(
            "TX-20260915-101500-A1B2C3D4");
        var originalHash = await ComputeSha256Async(context.OriginalPdfPath);
        var workingHash = await ComputeSha256Async(context.WorkingPdfPath);
        var pickedUpAt = new DateTimeOffset(
            2026,
            9,
            15,
            10,
            15,
            0,
            TimeSpan.FromHours(-6));
        var selectedId = context.Transaction.Devices[0].Id;

        var preparation = await context.Workflow.BeginPartialPickupAsync(
            context.Transaction.Id,
            [selectedId],
            "SSgt Synthetic Technician",
            "Synthetic first partial pickup.",
            pickedUpAt);

        await Assert.ThrowsAsync<SignatureValidationException>(
            () => context.Workflow.FinalizePartialPickupAsync(
                preparation.OperationId));

        var failed = await context.Journals.GetAsync(preparation.OperationId);
        Assert.NotNull(failed);
        Assert.Equal(WorkflowJournalService.FailedStatus, failed.Status);
        Assert.True(string.IsNullOrWhiteSpace(failed.DestinationPdfPath));
        Assert.True(string.IsNullOrWhiteSpace(failed.DestinationSha256));
        Assert.Empty(await context.Database.GetPickupReceiptsAsync(
            context.Transaction.Id));
        Assert.All(
            (await context.Database.GetTransactionByIdAsync(
                context.Transaction.Id))!.Devices,
            device => Assert.Equal(DeviceStatusCatalog.InShop, device.Status));

        await AppendSyntheticSignatureAsync(
            preparation.PreparedPdfPath,
            AppSettings.PickupSignatureFieldName,
            "Synthetic Pickup Signer");

        var recovery = await context.Workflow.ResumeWorkflowAsync(failed);

        Assert.True(recovery.Completed);
        var receipt = Assert.Single(
            await context.Database.GetPickupReceiptsAsync(
                context.Transaction.Id));
        Assert.Equal([selectedId], receipt.DeviceIds);
        Assert.Equal("Synthetic Pickup Signer", receipt.SignerName);
        Assert.True(File.Exists(receipt.PdfPath));
        Assert.NotEqual(preparation.PreparedPdfPath, receipt.PdfPath);
        Assert.True(File.Exists(preparation.PreparedPdfPath));
        Assert.Equal(originalHash, await ComputeSha256Async(
            context.OriginalPdfPath));
        Assert.Equal(workingHash, await ComputeSha256Async(
            context.WorkingPdfPath));

        var reloaded = await context.Database.GetTransactionByIdAsync(
            context.Transaction.Id);
        Assert.NotNull(reloaded);
        Assert.False(reloaded.IsArchived);
        Assert.Equal(context.WorkingPdfPath, reloaded.PdfPath);
        Assert.Equal(DeviceStatusCatalog.Returned, reloaded.Devices[0].Status);
        Assert.Equal(DeviceStatusCatalog.InShop, reloaded.Devices[1].Status);
        Assert.Equal(DeviceStatusCatalog.InShop, reloaded.Devices[2].Status);

        var artifacts = await context.Database.GetFileArtifactsAsync(
            context.Transaction.Id);
        Assert.Contains(
            artifacts,
            item => item.ArtifactType == "OriginalSignedIntake" &&
                    item.Path == context.OriginalPdfPath);
        Assert.Single(
            artifacts,
            item => item.ArtifactType ==
                    DatabaseService.SignedPartialPickupArtifactType);
    }

    [Fact]
    public async Task ResumeAfterCommitUsesProtectedReceiptWhenPreparedSourceIsMissing()
    {
        SQLitePCL.Batteries_V2.Init();
        await using var context = await WorkflowContext.CreateAsync(
            "TX-20260915-101501-A1B2C3D5");
        var selectedId = context.Transaction.Devices[1].Id;
        var preparation = await context.Workflow.BeginPartialPickupAsync(
            context.Transaction.Id,
            [selectedId],
            "SSgt Synthetic Technician",
            "Synthetic committed recovery.",
            DateTimeOffset.Now);
        await AppendSyntheticSignatureAsync(
            preparation.PreparedPdfPath,
            AppSettings.PickupSignatureFieldName,
            "Synthetic Pickup Signer");
        var firstResult = await context.Workflow.FinalizePartialPickupAsync(
            preparation.OperationId);
        Assert.False(firstResult.WasAlreadyCommitted);
        Assert.True(File.Exists(firstResult.PickupDocument.PdfPath));

        var journal = await context.Journals.GetAsync(
            preparation.OperationId);
        Assert.NotNull(journal);
        journal.Stage = "PickupReceiptCommitted";
        journal.Status = WorkflowJournalService.FailedStatus;
        journal.LastError = "Synthetic interruption after database commit.";
        await context.Journals.SaveAsync(journal);
        File.Delete(preparation.PreparedPdfPath);
        Assert.False(File.Exists(preparation.PreparedPdfPath));

        var auditCountBeforeRetry =
            (await context.Database.GetSnapshotAsync()).AuditRecords.Count;
        var recovery = await context.Workflow.ResumeWorkflowAsync(journal);

        Assert.True(recovery.Completed);
        Assert.Equal(firstResult.PickupDocument.PdfPath, recovery.PdfPath);
        Assert.True(File.Exists(recovery.PdfPath));
        Assert.Single(await context.Database.GetPickupReceiptsAsync(
            context.Transaction.Id));
        Assert.Equal(
            auditCountBeforeRetry,
            (await context.Database.GetSnapshotAsync()).AuditRecords.Count);
        Assert.Single(
            await context.Database.GetFileArtifactsAsync(
                context.Transaction.Id),
            item => item.ArtifactType ==
                    DatabaseService.SignedPartialPickupArtifactType);
        var completed = await context.Journals.GetAsync(
            preparation.OperationId);
        Assert.NotNull(completed);
        Assert.Equal(WorkflowJournalService.CompletedStatus, completed.Status);
    }

    [Fact]
    public async Task SuccessiveSubsetsUseFreshOriginalAndFinalReceiptArchivesParent()
    {
        SQLitePCL.Batteries_V2.Init();
        await using var context = await WorkflowContext.CreateAsync(
            "TX-20260915-101502-A1B2C3D6");
        var originalHash = await ComputeSha256Async(context.OriginalPdfPath);
        var workingHash = await ComputeSha256Async(context.WorkingPdfPath);
        var deviceIds = context.Transaction.Devices.Select(device => device.Id).ToArray();
        var firstTime = new DateTimeOffset(
            2026,
            9,
            15,
            11,
            0,
            0,
            TimeSpan.FromHours(-6));

        var first = await CompletePickupAsync(
            context,
            deviceIds[0],
            "Synthetic first subset.",
            firstTime);
        Assert.False(first.Archived);
        Assert.Equal(2, first.RemainingCount);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Workflow.BeginPartialPickupAsync(
                context.Transaction.Id,
                [deviceIds[0]],
                "SSgt Synthetic Technician",
                "Returned device must be rejected.",
                firstTime.AddMinutes(30)));

        var second = await CompletePickupAsync(
            context,
            deviceIds[2],
            "Synthetic second subset.",
            firstTime.AddHours(1));
        Assert.False(second.Archived);
        Assert.Equal(1, second.RemainingCount);

        var final = await CompletePickupAsync(
            context,
            deviceIds[1],
            "Synthetic final subset.",
            firstTime.AddHours(2));
        Assert.True(final.Archived);
        Assert.Equal(0, final.RemainingCount);

        var archived = await context.Database.GetTransactionByIdAsync(
            context.Transaction.Id);
        Assert.NotNull(archived);
        Assert.True(archived.IsArchived);
        Assert.Equal(final.PickupDocument.PdfPath, archived.PdfPath);
        Assert.All(
            archived.Devices,
            device => Assert.Equal(DeviceStatusCatalog.Returned, device.Status));

        var receipts = await context.Database.GetPickupReceiptsAsync(
            context.Transaction.Id);
        Assert.Equal(3, receipts.Count);
        Assert.Equal([1, 2, 3], receipts.Select(item => item.SequenceNumber));
        Assert.Equal(3, receipts.Select(item => item.PdfPath)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            [deviceIds[0], deviceIds[2], deviceIds[1]],
            receipts.SelectMany(item => item.DeviceIds));

        Assert.True(File.Exists(context.OriginalPdfPath));
        Assert.True(File.Exists(context.WorkingPdfPath));
        Assert.Equal(originalHash, await ComputeSha256Async(
            context.OriginalPdfPath));
        Assert.Equal(workingHash, await ComputeSha256Async(
            context.WorkingPdfPath));
        var artifacts = await context.Database.GetFileArtifactsAsync(
            context.Transaction.Id);
        Assert.Single(
            artifacts,
            item => item.ArtifactType == "OriginalSignedIntake" &&
                    item.Path == context.OriginalPdfPath);
        Assert.Equal(
            3,
            artifacts.Count(item => item.ArtifactType ==
                DatabaseService.SignedPartialPickupArtifactType));
    }

    [Fact]
    public async Task PendingPickupBlocksOtherWorkflowsAndRollbackPreservesAttempt()
    {
        SQLitePCL.Batteries_V2.Init();
        await using var context = await WorkflowContext.CreateAsync(
            "TX-20260915-101503-A1B2C3D7");
        var deviceIds = context.Transaction.Devices.Select(device => device.Id).ToArray();
        var workingHash = await ComputeSha256Async(context.WorkingPdfPath);
        var preparation = await context.Workflow.BeginPartialPickupAsync(
            context.Transaction.Id,
            [deviceIds[0]],
            "SSgt Synthetic Technician",
            "Synthetic attempt to abandon.",
            DateTimeOffset.Now);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Workflow.BeginPartialPickupAsync(
                context.Transaction.Id,
                [deviceIds[1]],
                "SSgt Synthetic Technician",
                "Blocked while another pickup is pending.",
                DateTimeOffset.Now));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Workflow.BeginCloseoutAsync(
                context.Transaction,
                DateTimeOffset.Now));
        Assert.Equal(workingHash, await ComputeSha256Async(
            context.WorkingPdfPath));

        await context.Workflow.RollbackWorkflowAsync(preparation.Journal);

        Assert.True(File.Exists(preparation.PreparedPdfPath));
        var abandoned = await context.Journals.GetAsync(
            preparation.OperationId);
        Assert.NotNull(abandoned);
        Assert.Equal(WorkflowJournalService.AbandonedStatus, abandoned.Status);
        var afterRollback = await context.Database.GetTransactionByIdAsync(
            context.Transaction.Id);
        Assert.NotNull(afterRollback);
        Assert.Null(afterRollback.CloseoutPreparedAt);
        Assert.All(
            afterRollback.Devices,
            device => Assert.Equal(DeviceStatusCatalog.InShop, device.Status));
        Assert.Contains(
            await context.Database.GetFileArtifactsAsync(
                context.Transaction.Id),
            item => item.ArtifactType == "AbandonedPartialPickupAttempt" &&
                    item.Path == preparation.PreparedPdfPath);

        var completed = await CompletePickupAsync(
            context,
            deviceIds[1],
            "Synthetic replacement selection.",
            DateTimeOffset.Now.AddMinutes(1));
        var completedJournal = await context.Journals.GetAsync(
            completed.PickupDocument.Id);
        Assert.NotNull(completedJournal);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Workflow.RollbackWorkflowAsync(completedJournal));

        var reloaded = await context.Database.GetTransactionByIdAsync(
            context.Transaction.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(DeviceStatusCatalog.InShop, reloaded.Devices[0].Status);
        Assert.Equal(DeviceStatusCatalog.Returned, reloaded.Devices[1].Status);
        Assert.Equal(DeviceStatusCatalog.InShop, reloaded.Devices[2].Status);
        Assert.Single(await context.Database.GetPickupReceiptsAsync(
            context.Transaction.Id));
    }

    private static async Task<PartialPickupResult> CompletePickupAsync(
        WorkflowContext context,
        long deviceId,
        string notes,
        DateTimeOffset pickedUpAt)
    {
        var preparation = await context.Workflow.BeginPartialPickupAsync(
            context.Transaction.Id,
            [deviceId],
            "SSgt Synthetic Technician",
            notes,
            pickedUpAt);
        await AppendSyntheticSignatureAsync(
            preparation.PreparedPdfPath,
            AppSettings.PickupSignatureFieldName,
            $"Synthetic Pickup Signer {preparation.SequenceNumber}");
        return await context.Workflow.FinalizePartialPickupAsync(
            preparation.OperationId);
    }

    private static async Task AppendSyntheticSignatureAsync(
        string pdfPath,
        string fieldName,
        string signerName)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            new X500DistinguishedName($"CN=\"{signerName}\""),
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature,
                critical: true));
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        var cms = new SignedCms(
            new ContentInfo(Encoding.UTF8.GetBytes(
                "Synthetic partial-pickup signature")),
            detached: false);
        cms.ComputeSignature(
            new CmsSigner(certificate)
            {
                IncludeOption = X509IncludeOption.EndCertOnly
            },
            silent: true);
        var signatureHex = Convert.ToHexString(cms.Encode());
        var escapedFieldName = fieldName
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
        var suffix = Encoding.Latin1.GetBytes(
            $"\n900000 0 obj << /FT /Sig /T ({escapedFieldName}) /V 900001 0 R >> endobj\n" +
            $"900001 0 obj << /Type /Sig /Contents <{signatureHex}> >> endobj\n");

        await using var stream = new FileStream(
            pdfPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(suffix);
        await stream.FlushAsync();
        stream.Flush(flushToDisk: true);
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private sealed class WorkflowContext : IAsyncDisposable
    {
        private WorkflowContext(
            string folder,
            DatabaseService database,
            WorkflowJournalService journals,
            TransactionWorkflowService workflow,
            EquipmentTransaction transaction,
            string originalPdfPath,
            string workingPdfPath)
        {
            Folder = folder;
            Database = database;
            Journals = journals;
            Workflow = workflow;
            Transaction = transaction;
            OriginalPdfPath = originalPdfPath;
            WorkingPdfPath = workingPdfPath;
        }

        public string Folder { get; }
        public DatabaseService Database { get; }
        public WorkflowJournalService Journals { get; }
        public TransactionWorkflowService Workflow { get; }
        public EquipmentTransaction Transaction { get; }
        public string OriginalPdfPath { get; }
        public string WorkingPdfPath { get; }

        public static async Task<WorkflowContext> CreateAsync(
            string transactionId)
        {
            var folder = Path.Combine(
                Path.GetTempPath(),
                "EquipmentTrackingTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var paths = new AppPaths(folder);
            var logger = new FileLogger(paths);
            var settings = new SettingsService(paths, logger);
            await settings.SaveAsync(new AppSettings
            {
                CompletedPdfFolder = Path.Combine(folder, "Completed"),
                ExcelExportPath = Path.Combine(
                    folder,
                    "Reports",
                    "EquipmentTracking.xlsx")
            });
            var backups = new BackupService(paths, logger);
            var database = new DatabaseService(paths, logger, backups);
            await database.InitializeAsync(createAutomaticBackup: false);
            var journals = new WorkflowJournalService(paths, logger);
            var pdf = new PdfFormService(logger);
            var workflow = new TransactionWorkflowService(
                paths,
                settings,
                pdf,
                new SignatureExtractionService(
                    new CertificateNameParser(),
                    logger),
                database,
                new ExcelExportService(database, settings, logger),
                new FileNameService(),
                journals,
                logger);

            var recordsFolder = Path.Combine(folder, "Records");
            Directory.CreateDirectory(recordsFolder);
            var originalPdfPath = Path.Combine(
                recordsFolder,
                "synthetic-original-signed-intake.pdf");
            var workingPdfPath = Path.Combine(
                recordsFolder,
                "synthetic-working-status.pdf");
            var fillResult = pdf.FillFields(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Templates",
                    "1297-58SOW-SC-TEMPLATE.pdf"),
                originalPdfPath,
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["TechnicianNameGrade"] = "A1C Synthetic Customer",
                    ["PhoneNumber"] = "555-0100",
                    ["Organization"] = "SYNTHETIC TEST UNIT",
                    ["TicketNumber"] = transactionId,
                    ["IssueDate"] = "09/15/2026",
                    ["ReturnDate"] = string.Empty,
                    ["Quantity"] = "3",
                    ["Device1"] =
                        "Model: Synthetic One\nPart: PART-1\nSerial: SERIAL-1",
                    ["Device2"] =
                        "Model: Synthetic Two\nPart: PART-2\nSerial: SERIAL-2",
                    ["Device3"] =
                        "Model: Synthetic Three\nPart: PART-3\nSerial: SERIAL-3"
                },
                settings.Current,
                transactionId);
            if (fillResult.MissingPdfFields.Count > 0)
            {
                throw new InvalidOperationException(
                    "The synthetic pickup workflow fixture is missing PDF fields.");
            }
            File.Copy(originalPdfPath, workingPdfPath);

            var now = DateTimeOffset.Now;
            var transaction = new EquipmentTransaction
            {
                Id = transactionId,
                Customer = new CustomerIdentity
                {
                    Rank = "A1C",
                    FirstName = "Synthetic",
                    LastName = "Customer",
                    OriginalName = "A1C Synthetic Customer"
                },
                PhoneNumber = "555-0100",
                Technician = "SSgt Synthetic Technician",
                Organization = "SYNTHETIC TEST UNIT",
                TicketNumber = transactionId,
                IssuedAt = now,
                CreatedAt = now,
                PdfPath = workingPdfPath,
                PdfSignerName = "Synthetic Customer",
                CertificateSubject = "CN=Synthetic Customer",
                CertificateThumbprint =
                    "00112233445566778899AABBCCDDEEFF00112233",
                SignatureTime = now,
                Devices = Enumerable.Range(1, 3)
                    .Select(index => new DeviceRecord
                    {
                        PartNumber = $"PART-{index}",
                        Model = $"Synthetic {index}",
                        SerialNumber = $"SERIAL-{index}",
                        AssetTag = $"ASSET-{index}",
                        Status = DeviceStatusCatalog.InShop
                    })
                    .ToList()
            };
            if (!await database.InsertTransactionAsync(transaction))
            {
                throw new InvalidOperationException(
                    "The synthetic pickup workflow fixture transaction was not inserted.");
            }
            var originalBytes = await File.ReadAllBytesAsync(originalPdfPath);
            await database.RecordFileArtifactAsync(
                transaction.Id,
                "OriginalSignedIntake",
                originalPdfPath,
                Convert.ToHexString(SHA256.HashData(originalBytes)),
                originalBytes.LongLength);

            return new WorkflowContext(
                folder,
                database,
                journals,
                workflow,
                transaction,
                originalPdfPath,
                workingPdfPath);
        }

        public ValueTask DisposeAsync()
        {
            Journals.Dispose();
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(Folder))
                {
                    Directory.Delete(Folder, recursive: true);
                }
            }
            catch
            {
                // Cleanup must not hide the test result.
            }
            return ValueTask.CompletedTask;
        }
    }
}
