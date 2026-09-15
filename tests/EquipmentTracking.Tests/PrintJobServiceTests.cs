using System.Security.Cryptography;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace EquipmentTracking.Tests;

public sealed class PrintJobServiceTests
{
    [Fact]
    public void TwoCopySheetIsOneLetterPageAndDoesNotModifySource()
    {
        var folder = CreateTemporaryFolder();
        try
        {
            var sourcePath = Path.Combine(folder, "selected-1297.pdf");
            File.Copy(GetIncludedTemplatePath(), sourcePath);

            var paths = new AppPaths(folder);
            var logger = new FileLogger(paths);
            var forms = new PdfFormService(logger);
            forms.UpdateLogicalFieldsInPlace(
                sourcePath,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PhoneNumber"] = "DSN 555-0100 / 505-555-0100",
                    ["TechnicianNameGrade"] = "TSgt Alexandra M. Print-Test Technician",
                    ["Organization"] = "58 SOW / SC",
                    ["TicketNumber"] = "INC-PRINT-2026-0001297",
                    ["IssueDate"] = "08/06/2026",
                    ["ReturnDate"] = string.Empty,
                    ["Quantity"] = "1",
                    ["Device1"] =
                        "Part Number: TEST-MODEL-WITH-A-LONG-PART-NUMBER\r\n" +
                        "Serial Number: TEST-SERIAL-WITH-A-LONG-SERIAL-NUMBER"
                },
                new AppSettings());

            var sourceHashBefore = ComputeSha256(sourcePath);
            var sourceFields = forms.ListFieldNames(sourcePath);
            var service = new PrintJobService(paths, logger);
            var outputPath = service.CreateTwoCopyLetterSheet(
                sourcePath,
                "PRINT-TEST-1297");

            Assert.True(File.Exists(outputPath));
            Assert.StartsWith(
                paths.PrintJobsDirectory,
                outputPath,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(sourceHashBefore, ComputeSha256(sourcePath));
            Assert.NotEmpty(sourceFields);
            Assert.Empty(forms.ListFieldNames(outputPath));

            using var document = PdfReader.Open(outputPath, PdfDocumentOpenMode.Modify);
            Assert.Equal(1, document.PageCount);
            var page = document.Pages[0];
            var crop = page.EffectiveCropBoxReadOnly;
            Assert.InRange(crop.Width, 611d, 613d);
            Assert.InRange(crop.Height, 791d, 793d);
            Assert.Null(document.Internals.Catalog.Elements["/AcroForm"]);

            var bottomStampCount = 0;
            var topStampCount = 0;
            for (var index = 0; index < page.Annotations.Count; index++)
            {
                var annotation = page.Annotations[index];
                Assert.False(string.Equals(
                    annotation?.Elements.GetName("/Subtype"),
                    "/Widget",
                    StringComparison.Ordinal));
                if (annotation is not null &&
                    string.Equals(
                        annotation.Elements.GetName("/Subtype"),
                        "/Stamp",
                        StringComparison.Ordinal) &&
                    (annotation.Elements.GetInteger("/F") & 4) != 0 &&
                    annotation.Elements["/AP"] is not null &&
                    annotation.Rectangle.Y1 >= -1d &&
                    annotation.Rectangle.Y2 <= 379d)
                {
                    bottomStampCount++;
                }
                else if (annotation is not null &&
                         string.Equals(
                             annotation.Elements.GetName("/Subtype"),
                             "/Stamp",
                             StringComparison.Ordinal) &&
                         (annotation.Elements.GetInteger("/F") & 4) != 0 &&
                         annotation.Elements["/AP"] is not null &&
                         annotation.Rectangle.Y1 >= 413d &&
                         annotation.Rectangle.Y2 <= 793d)
                {
                    topStampCount++;
                }
            }

            // Only the three signature widgets retain their exact PDF
            // appearances. Every /Tx field is redrawn in the uniform print
            // text layer, so none of its old thin appearances becomes a stamp.
            Assert.Equal(3, bottomStampCount);
            Assert.Equal(bottomStampCount, topStampCount);
        }
        finally
        {
            DeleteTemporaryFolder(folder);
        }
    }

    [Fact]
    public async Task TemporaryPrintJobIsDeletedAfterPrintingPromptCloses()
    {
        var folder = CreateTemporaryFolder();
        try
        {
            var sourcePath = Path.Combine(folder, "selected-1297.pdf");
            File.Copy(GetIncludedTemplatePath(), sourcePath);

            var paths = new AppPaths(folder);
            var logger = new FileLogger(paths);
            var forms = new PdfFormService(logger);
            forms.UpdateLogicalFieldsInPlace(
                sourcePath,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["TicketNumber"] = "DELETE-PRINT-TEST",
                    ["Quantity"] = "1",
                    ["Device1"] = "Part Number: MODEL-1\r\nSerial Number: SERIAL-1"
                },
                new AppSettings());

            var sourceHashBefore = ComputeSha256(sourcePath);
            var service = new PrintJobService(paths, logger);
            var outputPath = service.CreateTwoCopyLetterSheet(
                sourcePath,
                "DELETE-PRINT-TEST");

            Assert.True(File.Exists(outputPath));
            Assert.True(await service.DeleteTemporaryPrintJobAsync(outputPath));
            Assert.False(File.Exists(outputPath));
            Assert.True(File.Exists(sourcePath));
            Assert.Equal(sourceHashBefore, ComputeSha256(sourcePath));
        }
        finally
        {
            DeleteTemporaryFolder(folder);
        }
    }

    [Fact]
    public void Signed1297SignatureDictionaryDoesNotBreakPrintComposition()
    {
        var folder = CreateTemporaryFolder();
        try
        {
            var sourcePath = Path.Combine(folder, "signed-1297.pdf");
            File.Copy(GetIncludedTemplatePath(), sourcePath);

            var paths = new AppPaths(folder);
            var logger = new FileLogger(paths);
            var forms = new PdfFormService(logger);
            forms.UpdateLogicalFieldsInPlace(
                sourcePath,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["TicketNumber"] = "SIGNED-PRINT-TEST",
                    ["Quantity"] = "1",
                    ["Device1"] = "Part Number: MODEL-1\r\nSerial Number: SERIAL-1"
                },
                new AppSettings());
            AddSyntheticSignatureDictionary(sourcePath);

            var sourceHashBefore = ComputeSha256(sourcePath);
            var service = new PrintJobService(paths, logger);
            var outputPath = service.CreateTwoCopyLetterSheet(
                sourcePath,
                "SIGNED-PRINT-TEST");

            Assert.True(File.Exists(outputPath));
            Assert.Equal(sourceHashBefore, ComputeSha256(sourcePath));
        }
        finally
        {
            DeleteTemporaryFolder(folder);
        }
    }

    [Fact]
    public async Task TemporaryPrintCleanupRejectsAnOperationalPdf()
    {
        var folder = CreateTemporaryFolder();
        try
        {
            var sourcePath = Path.Combine(folder, "selected-1297.pdf");
            File.Copy(GetIncludedTemplatePath(), sourcePath);

            var paths = new AppPaths(folder);
            var service = new PrintJobService(paths, new FileLogger(paths));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.DeleteTemporaryPrintJobAsync(sourcePath));
            Assert.True(File.Exists(sourcePath));
        }
        finally
        {
            DeleteTemporaryFolder(folder);
        }
    }

    [Fact]
    public void FullLetterPdfIsRejectedAsWrong1297Layout()
    {
        var folder = CreateTemporaryFolder();
        try
        {
            var sourcePath = Path.Combine(folder, "full-letter.pdf");
            using (var document = new PdfDocument())
            {
                var page = document.AddPage();
                page.Width = XUnit.FromPoint(612d);
                page.Height = XUnit.FromPoint(792d);
                document.Save(sourcePath);
            }

            var paths = new AppPaths(folder);
            var service = new PrintJobService(paths, new FileLogger(paths));
            var exception = Assert.Throws<InvalidDataException>(() =>
                service.CreateTwoCopyLetterSheet(sourcePath, "WRONG-SIZE"));

            Assert.Contains("approved 8.5 by 5.25 inch", exception.Message);
        }
        finally
        {
            DeleteTemporaryFolder(folder);
        }
    }

    private static string GetIncludedTemplatePath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "Templates",
            "1297-58SOW-SC-TEMPLATE.pdf");

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void AddSyntheticSignatureDictionary(string path)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        PdfSharp.Pdf.Annotations.PdfAnnotation? signatureWidget = null;
        for (var index = 0; index < page.Annotations.Count; index++)
        {
            var annotation = page.Annotations[index];
            if (annotation is not null &&
                string.Equals(
                    annotation.Elements.GetName("/FT"),
                    "/Sig",
                    StringComparison.Ordinal))
            {
                signatureWidget = annotation;
                break;
            }
        }

        Assert.NotNull(signatureWidget);
        var signatureValue = new PdfDictionary(document);
        signatureValue.Elements.SetName("/Type", "/Sig");
        signatureValue.Elements.SetString("/Name", "Regression Test Signer");
        document.Internals.AddObject(signatureValue);
        signatureWidget!.Elements["/V"] = signatureValue.ReferenceNotNull;
        document.Save(path);
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
