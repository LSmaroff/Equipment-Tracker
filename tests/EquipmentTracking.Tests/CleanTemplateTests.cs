using System.Security.Cryptography;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;

namespace EquipmentTracking.Tests;

public sealed class CleanTemplateTests
{
    [Fact]
    public void IncludedTemplateHasExpectedFieldsAndNoJavaScriptOrNeedAppearances()
    {
        var template = Path.Combine(AppContext.BaseDirectory, "Templates", "1297-58SOW-SC-TEMPLATE.pdf");
        Assert.True(File.Exists(template), $"Template was not copied to the test output: {template}");

        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "EquipmentTrackingTests", Guid.NewGuid().ToString("N")));
        var service = new PdfFormService(new FileLogger(paths));
        var settings = new AppSettings();
        var inspection = service.InspectTemplate(template, settings.PdfFieldMappings.Values.ToArray());

        Assert.Empty(inspection.MissingRequiredFields);
        Assert.False(inspection.NeedAppearances);
        Assert.False(inspection.ContainsJavaScriptMarkers);
        Assert.Equal(3, inspection.SignatureFieldCount);
        Assert.Contains("Pickup Signature", inspection.FieldNames);
    }


    [Fact]
    public async Task IncludedTemplateMatchesTheApprovedCleanTemplateHash()
    {
        var template = Path.Combine(AppContext.BaseDirectory, "Templates", "1297-58SOW-SC-TEMPLATE.pdf");
        await using var stream = File.OpenRead(template);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));

        Assert.Equal(
            "00DAA4AC652D592F03554E1D8E2EA9EC6083B30E9C9659330FE1DAD32E599051",
            hash);
    }

    [Fact]
    public void IncludedTemplateCanBeFilledWithoutFlatteningFields()
    {
        var template = Path.Combine(AppContext.BaseDirectory, "Templates", "1297-58SOW-SC-TEMPLATE.pdf");
        var folder = Path.Combine(Path.GetTempPath(), "EquipmentTrackingTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var output = Path.Combine(folder, "filled.pdf");
            var paths = new AppPaths(folder);
            var service = new PdfFormService(new FileLogger(paths));
            var settings = new AppSettings();
            var result = service.FillFields(
                template,
                output,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PhoneNumber"] = "555-0100",
                    ["TechnicianNameGrade"] = "SSgt Tester",
                    ["Organization"] = "58 SOW",
                    ["TicketNumber"] = "INC-TEST",
                    ["IssueDate"] = "07/01/2026",
                    ["ReturnDate"] = string.Empty,
                    ["Quantity"] = "1",
                    ["Device1"] = "Model Name: Test Model\\r\\nPart Number: TEST\\r\\nSerial Number: SERIAL-1"
                },
                settings,
                "TX-20260810-143025-1A2B3C4D");

            Assert.True(File.Exists(output));
            Assert.True(result.FieldsFilled >= 8);
            var fields = service.ListFieldNames(output);
            Assert.Contains("Device1", fields);
            Assert.Contains("Pickup Signature", fields);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch
            {
            }
        }
    }
}
