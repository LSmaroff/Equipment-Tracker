namespace EquipmentTracking.App.Models;

public sealed class BarcodeParseResult
{
    public bool Parsed { get; init; }
    public string PartNumber { get; init; } = string.Empty;
    public string Model => PartNumber;
    public string SerialNumber { get; init; } = string.Empty;
    public string CageCode { get; init; } = string.Empty;
    public string AssetTag { get; init; } = string.Empty;
    public string RawValue { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
