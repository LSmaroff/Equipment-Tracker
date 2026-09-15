using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;

namespace EquipmentTracking.Tests;

public sealed class PdfLogicalValueBuilderTests
{
    [Fact]
    public void Build_UsesOnlyApplicationManagedLogicalFields()
    {
        var devices = new[]
        {
            new DeviceRecord
            {
                Model = "Acer Mustang T630",
                PartNumber = "AC-MSTNGT630022",
                SerialNumber = "1391590010156",
                AssetTag = "TAG-001"
            }
        };

        var values = PdfLogicalValueBuilder.Build(
            "555-0100",
            "MSgt Testing, Test T",
            "58 SOW",
            "INC123456",
            devices,
            issueDate: new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal("555-0100", values["PhoneNumber"]);
        Assert.Equal("MSgt Testing, Test T", values["TechnicianNameGrade"]);
        Assert.Equal("58 SOW", values["Organization"]);
        Assert.Equal("INC123456", values["TicketNumber"]);
        Assert.Equal(
            "Model Name: Acer Mustang T630\r\n" +
            "Part Number: AC-MSTNGT630022\r\n" +
            "Serial Number: 1391590010156\r\n" +
            "Asset Tag: TAG-001",
            values["Device1"]);

        Assert.Equal("07/01/2026", values["IssueDate"]);
        Assert.Equal(string.Empty, values["ReturnDate"]);
        Assert.Equal("1", values["Quantity"]);
        Assert.DoesNotContain("TechnicianSignature", values.Keys);
        Assert.DoesNotContain("CustomerSignature", values.Keys);
        Assert.DoesNotContain("PickupSignature", values.Keys);
    }

    [Fact]
    public void BuildDeviceBlock_OmitsBlankAssetTag()
    {
        var block = PdfLogicalValueBuilder.BuildDeviceBlock(new DeviceRecord
        {
            PartNumber = "PART-1",
            SerialNumber = "SERIAL-1",
            AssetTag = " "
        });

        Assert.Equal(
            "Part Number: PART-1\r\nSerial Number: SERIAL-1",
            block);
    }
}
