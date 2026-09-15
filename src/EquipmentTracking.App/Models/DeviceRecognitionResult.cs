namespace EquipmentTracking.App.Models;

public sealed class DeviceRecognitionResult
{
    public string PartNumber { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
}
