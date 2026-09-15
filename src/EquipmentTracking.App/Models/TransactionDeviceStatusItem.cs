using EquipmentTracking.App.Infrastructure;

namespace EquipmentTracking.App.Models;

public sealed class TransactionDeviceStatusItem : ObservableObject
{
    private string _status = string.Empty;
    private bool _isPickedUp;

    public long DeviceId { get; init; }
    public string TransactionId { get; init; } = string.Empty;
    public int DeviceNumber { get; init; }
    public string Model { get; init; } = string.Empty;
    public string PartNumber { get; init; } = string.Empty;
    public string AssetTag { get; init; } = string.Empty;
    public string SerialNumber { get; init; } = string.Empty;
    public string OriginalStatus { get; init; } = string.Empty;

    public string PdfLogicalFieldName => $"Device{DeviceNumber}";
    public bool IsAlreadyReturned => string.Equals(OriginalStatus, "Returned", StringComparison.OrdinalIgnoreCase);
    public bool CanChange => !IsAlreadyReturned;
    public bool CanEditStatus => CanChange && !IsPickedUp;
    public string DisplayStatus => IsPickedUp ? "Pickup selected" : Status;

    public string Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(DisplayStatus));
            }
        }
    }

    public bool IsPickedUp
    {
        get => _isPickedUp;
        set
        {
            if (SetProperty(ref _isPickedUp, value))
            {
                OnPropertyChanged(nameof(CanEditStatus));
                OnPropertyChanged(nameof(DisplayStatus));
            }
        }
    }
}
