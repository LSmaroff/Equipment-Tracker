namespace EquipmentTracking.App.Models;

public sealed class DeviceIdentityHistoryItem
{
    public string PartNumber { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public string RawScanValue { get; init; } = string.Empty;
}
