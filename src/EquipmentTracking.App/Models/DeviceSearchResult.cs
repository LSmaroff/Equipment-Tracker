using EquipmentTracking.App.Infrastructure;

namespace EquipmentTracking.App.Models;

public sealed class DeviceSearchResult : ObservableObject
{
    private bool _isSameTransactionAsSelection;
    private string _status = string.Empty;

    public long DeviceId { get; init; }
    public string TransactionId { get; init; } = string.Empty;
    public string TicketNumber { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string PartNumber { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string SerialNumber { get; init; } = string.Empty;
    public string AssetTag { get; init; } = string.Empty;
    public DateTimeOffset IssuedAt { get; init; }
    public string PdfPath { get; init; } = string.Empty;

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value?.Trim() ?? string.Empty);
    }

    public bool IsSameTransactionAsSelection
    {
        get => _isSameTransactionAsSelection;
        set => SetProperty(ref _isSameTransactionAsSelection, value);
    }
}
