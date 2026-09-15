namespace EquipmentTracking.App.Models;

public sealed class DeviceStatusUpdate
{
    public long DeviceId { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool MarkReturned { get; init; }
}
