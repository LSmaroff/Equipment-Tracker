using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;
using Microsoft.Data.Sqlite;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;

namespace EquipmentTracking.Tests;

public sealed class TransactionWorkflowServiceTests
{
    [Fact]
    public async Task FinalizeAsync_PreservesEnteredCustomerWhenPdfSignerIsDifferent()
    {
        SQLitePCL.Batteries_V2.Init();
        var folder = CreateTemporaryFolder();

        try
        {
            var paths = new AppPaths(folder);
            var logger = new FileLogger(paths);
            var settings = new SettingsService(paths, logger);
            var appSettings = new AppSettings
            {
                CompletedPdfFolder = Path.Combine(folder, "Completed"),
                ExcelExportPath = Path.Combine(folder, "Reports", "EquipmentTracking.xlsx")
            };
            await settings.SaveAsync(appSettings);

            var backups = new BackupService(paths, logger);
            var database = new DatabaseService(paths, logger, backups);
            await database.InitializeAsync(createAutomaticBackup: false);

            using var journals = new WorkflowJournalService(paths, logger);
            var workflow = new TransactionWorkflowService(
                paths,
                settings,
                new PdfFormService(logger),
                new SignatureExtractionService(new CertificateNameParser(), logger),
                database,
                new ExcelExportService(database, settings, logger),
                new FileNameService(),
                journals,
                logger);

            const string transactionId = "TX-20260810-143025-1A2B3C4D";
            var workingFolder = Path.Combine(paths.WorkingDirectory, transactionId);
            Directory.CreateDirectory(workingFolder);
            var templateCopyPath = Path.Combine(workingFolder, "Template.pdf");
            var preparedPdfPath = Path.Combine(workingFolder, "Prepared.pdf");
            var enteredCustomer = new CustomerIdentity
            {
                Rank = "MSgt",
                FirstName = "Test",
                MiddleInitial = "T",
                LastName = "Testing",
                OriginalName = "MSgt Test T Testing"
            };
            Assert.Equal("MSgt Testing, Test T", enteredCustomer.DisplayName);
            var working = new WorkingTransaction
            {
                TransactionId = transactionId,
                CreatedAt = DateTimeOffset.Now,
                WorkingFolder = workingFolder,
                TemplateCopyPath = templateCopyPath,
                PreparedPdfPath = preparedPdfPath
            };
            var devices = new[]
            {
                new DeviceRecord
                {
                    PartNumber = "PART-001",
                    Model = "MODEL-001",
                    SerialNumber = "SERIAL-IDENTITY-TEST"
                }
            };

            var approvedTemplate = Path.Combine(
                AppContext.BaseDirectory,
                "Templates",
                "1297-58SOW-SC-TEMPLATE.pdf");
            Assert.True(File.Exists(approvedTemplate));
            File.Copy(approvedTemplate, templateCopyPath);
            workflow.PreparePdf(
                working,
                enteredCustomer,
                "555-0100",
                "SSgt Technician",
                "58 SOW",
                "INC-IDENTITY-TEST",
                devices);

            using (var preparedDocument = PdfReader.Open(
                       preparedPdfPath,
                       PdfDocumentOpenMode.Modify))
            {
                var issuedToName = Assert.IsType<PdfTextField>(
                    preparedDocument.AcroForm?.Fields["TechnicianName"]);
                Assert.Equal("MSgt Testing, Test T", issuedToName.Text);
            }

            await File.WriteAllTextAsync(
                preparedPdfPath,
                CreateSyntheticSignedPdf("MSgt Smaroff, Liam D"),
                Encoding.Latin1);

            var result = await workflow.FinalizeAsync(
                working,
                enteredCustomer,
                "555-0100",
                "SSgt Technician",
                "58 SOW",
                "INC-IDENTITY-TEST",
                devices,
                selectedCertificate: null);

            var saved = await database.GetTransactionByIdAsync(transactionId);
            Assert.NotNull(saved);
            Assert.Equal("MSgt", saved.Customer.Rank);
            Assert.Equal("Test", saved.Customer.FirstName);
            Assert.Equal("T", saved.Customer.MiddleInitial);
            Assert.Equal("Testing", saved.Customer.LastName);
            Assert.Equal("MSgt Test T Testing", saved.Customer.OriginalName);
            Assert.Equal("MSgt Testing, Test T", saved.Customer.DisplayName);
            Assert.Equal("MSgt Smaroff, Liam D", result.Signature.SignerName);
            Assert.Equal("MSgt Smaroff, Liam D", saved.PdfSignerName);
            Assert.Contains("MSgt Smaroff", saved.CertificateSubject, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(saved.CertificateThumbprint));
            Assert.StartsWith(
                "test.t.testing-",
                Path.GetFileName(result.FinalPdfPath),
                StringComparison.Ordinal);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteTemporaryFolder(folder);
        }
    }

    private static string CreateSyntheticSignedPdf(string signerName)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            new X500DistinguishedName($"CN=\"{signerName}\""),
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        var cms = new SignedCms(
            new ContentInfo(Encoding.UTF8.GetBytes("Synthetic 1297 signature")),
            detached: false);
        var signer = new CmsSigner(certificate)
        {
            IncludeOption = X509IncludeOption.EndCertOnly
        };
        cms.ComputeSignature(signer, silent: true);
        var signatureHex = Convert.ToHexString(cms.Encode());

        return
            "%PDF-1.7\n" +
            "1 0 obj << /FT /Sig /T (ISSUED TO SIGNATURE) /V 2 0 R >> endobj\n" +
            $"2 0 obj << /Type /Sig /Contents <{signatureHex}> >> endobj\n" +
            "%%EOF";
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
            // Cleanup must not hide the test result.
        }
    }
}
