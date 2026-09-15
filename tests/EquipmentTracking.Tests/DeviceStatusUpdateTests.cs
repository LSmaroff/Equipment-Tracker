using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;
using EquipmentTracking.App.ViewModels;

namespace EquipmentTracking.Tests;

public sealed class DeviceStatusUpdateTests
{
    [Fact]
    public void StatusCatalog_ContainsEverySupportedStatus()
    {
        Assert.Contains(DeviceStatusCatalog.InShop, DeviceStatusCatalog.Values);
        Assert.Contains(DeviceStatusCatalog.ReImaging, DeviceStatusCatalog.Values);
        Assert.Contains(DeviceStatusCatalog.ReadyForPickup, DeviceStatusCatalog.Values);
        Assert.Contains(DeviceStatusCatalog.Troubleshooting, DeviceStatusCatalog.Values);
        Assert.Contains(DeviceStatusCatalog.Returned, DeviceStatusCatalog.Values);
        Assert.True(DeviceStatusCatalog.IsValidStatus(DeviceStatusCatalog.Returned));
    }

    [Fact]
    public void BuildUpdates_PickedUpDevice_ForcesReturnedStatus()
    {
        var transaction = new RecentTransactionItem
        {
            TransactionId = "transaction-1",
            TicketNumber = "TICKET-1",
            Technician = "A1C Example, Technician"
        };
        var device = new TransactionDeviceStatusItem
        {
            DeviceId = 10,
            TransactionId = transaction.TransactionId,
            DeviceNumber = 1,
            Model = "MODEL-1",
            SerialNumber = "SERIAL-1",
            OriginalStatus = DeviceStatusCatalog.InShop,
            Status = DeviceStatusCatalog.InShop,
            IsPickedUp = true
        };
        var viewModel = new DeviceStatusDialogViewModel(
            transaction,
            [device],
            [transaction.Technician]);

        var update = Assert.Single(viewModel.BuildUpdates());

        Assert.Equal(DeviceStatusCatalog.Returned, update.Status);
        Assert.True(update.MarkReturned);
    }
}
