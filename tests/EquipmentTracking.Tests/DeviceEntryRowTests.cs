using EquipmentTracking.App.Models;

namespace EquipmentTracking.Tests;

public sealed class DeviceEntryRowTests
{
    [Fact]
    public void ModelName_PreservesSpacesDuringProgressiveTyping()
    {
        var row = new DeviceEntryRow();

        foreach (var value in new[]
                 {
                     "EliteBook",
                     "EliteBook ",
                     "EliteBook 8",
                     "EliteBook 830 ",
                     "EliteBook 830 G8"
                 })
        {
            row.ModelName = value;
            Assert.Equal(value, row.ModelName);
        }
    }
}
