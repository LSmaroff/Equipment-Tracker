using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;
using EquipmentTracking.App.ViewModels;

namespace EquipmentTracking.Tests;

public sealed class PartialPickupSelectionTests
{
    [Fact]
    public void ScanIuid_SelectsExactSerial_AndDoesNotToggleRepeatedScans()
    {
        var selected = Device(1, "2MQ5390WTS");
        var other = Device(2, "2MQ5390WTS-OTHER");
        var viewModel = Dialog(selected, other);
        viewModel.ScanInput = ">«RS»06«GS»18S7ESQ72MQ5390WTS";
        Assert.True(viewModel.SelectScannedDevice());
        Assert.True(selected.IsPickedUp);
        Assert.False(other.IsPickedUp);
        Assert.Equal(string.Empty, viewModel.ScanInput);
        viewModel.ScanInput = " 2mq5390wts ";
        Assert.True(viewModel.SelectScannedDevice());
        Assert.Equal(1, viewModel.PickupCount);
        Assert.Equal(1, viewModel.RemainingCount);
        Assert.Contains("already selected", viewModel.ScanFeedback);
    }

    [Fact]
    public void UnknownOrPartialSerial_DoesNotSelectAnyDevice()
    {
        var viewModel = Dialog(Device(1, "SERIAL-LONG"), Device(2, "OTHER"));
        viewModel.ScanInput = "SERIAL";
        Assert.False(viewModel.SelectScannedDevice());
        Assert.Equal(0, viewModel.PickupCount);
        Assert.Empty(viewModel.BuildUpdates());
    }

    [Fact]
    public void AmbiguousAssetTag_RequiresManualSelection()
    {
        var viewModel = Dialog(Device(1, "SERIAL-1", assetTag: "ASSET-1"),
            Device(2, "SERIAL-2", assetTag: "ASSET-1"));
        viewModel.ScanInput = "ASSET-1";
        Assert.False(viewModel.SelectScannedDevice());
        Assert.Equal(0, viewModel.PickupCount);
        Assert.Contains("more than one", viewModel.ScanFeedback);
    }

    [Fact]
    public void ReturnedDevice_CannotBeSelectedAgain_AndKeepsOriginalRowNumber()
    {
        var returned = Device(1, "RETURNED", DeviceStatusCatalog.Returned);
        var remaining = Device(2, "REMAINING");
        var viewModel = Dialog(returned, remaining);
        viewModel.ScanInput = "RETURNED";
        Assert.False(viewModel.SelectScannedDevice());
        Assert.False(returned.IsPickedUp);
        viewModel.ScanInput = "REMAINING";
        Assert.True(viewModel.SelectScannedDevice());
        Assert.Equal("Device2", Assert.Single(viewModel.GetPickedUpLogicalFields()));
        Assert.Equal(0, viewModel.RemainingCount);
        Assert.Contains("Archive", viewModel.PickupGuidance);
    }

    [Fact]
    public void PickupOnly_DoesNotIncludeUnrelatedStatusEdits()
    {
        var selected = Device(1, "ONE");
        var other = Device(2, "TWO");
        var viewModel = Dialog(selected, other);
        selected.IsPickedUp = true;
        other.Status = DeviceStatusCatalog.Troubleshooting;
        var update = Assert.Single(viewModel.BuildUpdates());
        Assert.Equal(selected.DeviceId, update.DeviceId);
        Assert.Equal(DeviceStatusCatalog.Returned, update.Status);
        Assert.True(update.MarkReturned);
        Assert.False(viewModel.CanEditWorkingStatus);
        Assert.False(selected.CanEditStatus);
        Assert.Equal("Prepare pickup copy", viewModel.SaveLabel);
    }

    [Fact]
    public void ReadySelection_IsAdditive_AndClearDoesNotChangeWorkingStatuses()
    {
        var ready = Device(1, "READY", DeviceStatusCatalog.ReadyForPickup);
        var working = Device(2, "WORKING");
        var returned = Device(3, "RETURNED", DeviceStatusCatalog.Returned);
        var viewModel = Dialog(ready, working, returned);
        working.IsPickedUp = true;
        viewModel.SelectReadyCommand.Execute(null);
        Assert.Equal(2, viewModel.PickupCount);
        Assert.False(returned.IsPickedUp);
        viewModel.ClearPickupSelectionCommand.Execute(null);
        Assert.Equal(0, viewModel.PickupCount);
        Assert.Equal(2, viewModel.RemainingCount);
        Assert.Equal(DeviceStatusCatalog.ReadyForPickup, ready.Status);
        Assert.Equal(DeviceStatusCatalog.InShop, working.Status);
    }

    [Fact]
    public void SelectionChanges_NotifySummaryAndActionLabel()
    {
        var selected = Device(1, "ONE");
        var viewModel = Dialog(selected);
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        selected.IsPickedUp = true;
        Assert.Contains(nameof(viewModel.PickupSummary), notifications);
        Assert.Contains(nameof(viewModel.PickupGuidance), notifications);
        Assert.Contains(nameof(viewModel.SaveLabel), notifications);
    }

    private static DeviceStatusDialogViewModel Dialog(params TransactionDeviceStatusItem[] devices) =>
        new(new RecentTransactionItem { TransactionId = "SYNTHETIC-1297" }, devices,
            ["TSgt Synthetic Technician"], pickupOnly: true);

    private static TransactionDeviceStatusItem Device(int number, string serial,
        string status = DeviceStatusCatalog.InShop, string assetTag = "") => new()
        {
            DeviceId = number,
            TransactionId = "SYNTHETIC-1297",
            DeviceNumber = number,
            SerialNumber = serial,
            AssetTag = assetTag,
            PartNumber = "SYNTHETIC-PART",
            Model = "Synthetic Device",
            Status = status,
            OriginalStatus = status
        };
}
