using System.Globalization;
using System.Security.Cryptography;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;
using PdfSharp.Pdf.IO;

namespace EquipmentTracking.Tests;

public sealed class PartialPickupPdfLayoutTests
{
    [Fact]
    public void SuccessiveCopies_PreserveOriginalAndInteractiveFields_AndUseFreshSubset()
    {
        var folder = Path.Combine(Path.GetTempPath(), "EquipmentTrackingTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var service = new PdfFormService(new FileLogger(new AppPaths(folder)));
            var settings = new AppSettings();
            var original = Path.Combine(folder, "synthetic-original.pdf");
            var first = Path.Combine(folder, "synthetic-pickup-1.pdf");
            var second = Path.Combine(folder, "synthetic-pickup-2.pdf");
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["TechnicianNameGrade"] = "MSgt Test Customer",
                ["PhoneNumber"] = "555-0100",
                ["Organization"] = "TEST UNIT",
                ["TicketNumber"] = "SYNTHETIC-1297",
                ["IssueDate"] = "09/15/2026", ["ReturnDate"] = "", ["Quantity"] = "3",
                ["Device1"] = "HP 645 / SYN-ONE",
                ["Device2"] = "LAPTOP / SYN-TWO",
                ["Device3"] = "MONITOR / SYN-THREE"
            };
            var filled = service.FillFields(Path.Combine(AppContext.BaseDirectory, "Templates", "1297-58SOW-SC-TEMPLATE.pdf"),
                original, values, settings, "TX-20260915-120000-1234ABCD");
            Assert.Empty(filled.MissingPdfFields);
            var originalHash = SHA256.HashData(File.ReadAllBytes(original));
            var pickupDate = new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
            service.CreatePartialPickupPdf(original, first, ["Device1"], pickupDate, settings);
            service.CreatePartialPickupPdf(original, second, ["Device3"], pickupDate.AddDays(1), settings);
            Assert.Equal(originalHash, SHA256.HashData(File.ReadAllBytes(original)));
            foreach (var copy in new[] { first, second })
            {
                var inspection = service.InspectTemplate(copy, settings.PdfFieldMappings.Values.ToArray());
                Assert.Empty(inspection.MissingRequiredFields);
                Assert.Equal(3, inspection.SignatureFieldCount);
                Assert.False(inspection.NeedAppearances);
                using var document = PdfReader.Open(copy, PdfDocumentOpenMode.Modify);
                Assert.NotNull(document.AcroForm);
                foreach (var logicalField in new[] { "Device1", "Device2", "Device3" })
                {
                    var field = document.AcroForm.Fields[logicalField];
                    Assert.NotNull(field);
                    Assert.Equal(values[logicalField], field.Elements.GetString("/V"));
                }
                var expectedDate = copy == first ? pickupDate : pickupDate.AddDays(1);
                var returnDateField = document.AcroForm.Fields["RETURN DATE"];
                Assert.NotNull(returnDateField);
                Assert.Equal(expectedDate.ToLocalTime().ToString("MM/dd/yyyy", CultureInfo.InvariantCulture),
                    returnDateField.Elements.GetString("/V"));
            }
            Assert.False(File.Exists(Path.Combine(folder, "synthetic-pickup-1-signed-original.pdf")));
            var qaOutput = Environment.GetEnvironmentVariable("ETP_PICKUP_QA_OUTPUT");
            if (!string.IsNullOrWhiteSpace(qaOutput))
            {
                Directory.CreateDirectory(qaOutput);
                foreach (var file in new[] { original, first, second })
                    File.Copy(file, Path.Combine(qaOutput, Path.GetFileName(file)), overwrite: true);
            }
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
