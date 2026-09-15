using EquipmentTracking.App.Models;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Services;

namespace EquipmentTracking.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    
    public void DefaultMappings_MatchSupplied1297FieldNames()
    {
        var mappings = AppSettings.CreateDefaultPdfFieldMappings();

        Assert.Equal("Pickup Signature", AppSettings.PickupSignatureFieldName);

        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TechnicianSignature"] = "ISSUED BY SIGNATURE",
            ["PhoneNumber"] = "DUTY PHONE",
            ["CustomerSignature"] = "ISSUED TO SIGNATURE",
            ["TechnicianNameGrade"] = "TechnicianName",
            ["Organization"] = "ORGN",
            ["IssueDate"] = "DATE OF ISSUE",
            ["ReturnDate"] = "RETURN DATE",
            ["TicketNumber"] = "TicketNumber",
            ["PickupSignature"] = "Pickup Signature",
            ["Quantity"] = "QNTY"
        };

        for (var index = 1; index <= 10; index++)
        {
            expected[$"Device{index}"] = $"Device{index}";
        }

        Assert.Equal(expected.Count, mappings.Count);
        foreach (var pair in expected)
        {
            Assert.True(mappings.TryGetValue(pair.Key, out var actual));
            Assert.Equal(pair.Value, actual);
        }
    }

    [Fact]
    public void DefaultSettings_ContainAtLeastOneOrganization()
    {
        var settings = new AppSettings();

        Assert.NotEmpty(settings.Organizations);
        Assert.All(settings.Organizations, organization => Assert.False(string.IsNullOrWhiteSpace(organization)));
        Assert.InRange(settings.MaximumDevicesPerForm, 1, 10);
    }

    [Fact]
    public async Task LoadAsync_RepairsLegacyReversedSignatureMappingsForApprovedTemplate()
    {
        var folder = CreateTemporaryFolder();
        try
        {
            var paths = new AppPaths(folder);
            var logger = new FileLogger(paths);
            var legacy = new AppSettings();
            legacy.PdfFieldMappings["TechnicianSignature"] = "ISSUED TO SIGNATURE";
            legacy.PdfFieldMappings["CustomerSignature"] = "ISSUED BY SIGNATURE";
            await new SettingsService(paths, logger).SaveAsync(legacy);

            var reloaded = new SettingsService(paths, logger);
            await reloaded.LoadAsync();

            Assert.Equal(
                "ISSUED BY SIGNATURE",
                reloaded.Current.PdfFieldMappings["TechnicianSignature"]);
            Assert.Equal(
                "ISSUED TO SIGNATURE",
                reloaded.Current.PdfFieldMappings["CustomerSignature"]);
        }
        finally
        {
            DeleteTemporaryFolder(folder);
        }
    }

    [Fact]
    public async Task LoadAsync_PreservesSignatureMappingsForCustomTemplate()
    {
        var folder = CreateTemporaryFolder();
        try
        {
            var paths = new AppPaths(folder);
            var logger = new FileLogger(paths);
            var custom = new AppSettings { TemplatePdfPath = @"Templates\Custom-1297.pdf" };
            custom.PdfFieldMappings["TechnicianSignature"] = "ISSUED TO SIGNATURE";
            custom.PdfFieldMappings["CustomerSignature"] = "ISSUED BY SIGNATURE";
            await new SettingsService(paths, logger).SaveAsync(custom);

            var reloaded = new SettingsService(paths, logger);
            await reloaded.LoadAsync();

            Assert.Equal(
                "ISSUED TO SIGNATURE",
                reloaded.Current.PdfFieldMappings["TechnicianSignature"]);
            Assert.Equal(
                "ISSUED BY SIGNATURE",
                reloaded.Current.PdfFieldMappings["CustomerSignature"]);
        }
        finally
        {
            DeleteTemporaryFolder(folder);
        }
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
