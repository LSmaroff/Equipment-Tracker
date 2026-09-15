using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;

namespace EquipmentTracking.Tests;

public sealed class ThemeAndStatusTests
{
    [Theory]
    [InlineData("Dark", "Dark")]
    [InlineData("dark", "Dark")]
    [InlineData("LIGHT", "Light")]
    [InlineData("System", "System")]
    [InlineData("", "Dark")]
    [InlineData(null, "Dark")]
    public void ThemeNormalize_ReturnsSupportedValue(string? input, string expected)
    {
        Assert.Equal(expected, ThemeService.Normalize(input));
    }

    [Fact]
    public void DefaultSettings_UseProfessionalDarkTheme()
    {
        Assert.Equal("Dark", new AppSettings().Theme);
    }

    [Theory]
    [InlineData(DeviceStatusCatalog.InShop)]
    [InlineData(DeviceStatusCatalog.ReImaging)]
    [InlineData(DeviceStatusCatalog.ReadyForPickup)]
    [InlineData(DeviceStatusCatalog.Troubleshooting)]
    public void ActiveStatusCatalog_AcceptsConfiguredStatuses(string status)
    {
        Assert.True(DeviceStatusCatalog.IsValidActiveStatus(status));
    }

    [Fact]
    public void ActiveStatusCatalog_DoesNotTreatReturnedAsActive()
    {
        Assert.False(DeviceStatusCatalog.IsValidActiveStatus(DeviceStatusCatalog.Returned));
    }

    [Fact]
    public void DeviceSearchResult_Status_IsObservable()
    {
        var result = new DeviceSearchResult { Status = DeviceStatusCatalog.InShop };
        string? changedProperty = null;
        result.PropertyChanged += (_, args) => changedProperty = args.PropertyName;

        result.Status = DeviceStatusCatalog.ReadyForPickup;

        Assert.Equal(DeviceStatusCatalog.ReadyForPickup, result.Status);
        Assert.Equal(nameof(DeviceSearchResult.Status), changedProperty);
    }
}
