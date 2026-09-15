using EquipmentTracking.App.Models;

namespace EquipmentTracking.App.Services;

public sealed class KnownDeviceCatalog
{
    private const string HpEliteBook645PartNumber = "A4TH1AV";
    private const string HpEliteBook645ModelName = "HP EliteBook 645";

    private static readonly Dictionary<string, DeviceRecognitionResult> ExactDevices =
        new Dictionary<string, DeviceRecognitionResult>(StringComparer.OrdinalIgnoreCase)
        {
            [BuildDeviceKey("7ESQ7", "2MQ5390WTS")] = new()
            {
                PartNumber = HpEliteBook645PartNumber,
                ModelName = HpEliteBook645ModelName,
                Source = "Exact built-in device identity"
            }
        };

    private static readonly Dictionary<string, string> PartModels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [HpEliteBook645PartNumber] = HpEliteBook645ModelName
        };

    public bool TryResolveExactDevice(
        string? cageCode,
        string? serialNumber,
        out DeviceRecognitionResult result)
    {
        var key = BuildDeviceKey(cageCode, serialNumber);
        if (key.Length > 1 && ExactDevices.TryGetValue(key, out var known))
        {
            result = known;
            return true;
        }

        result = new DeviceRecognitionResult();
        return false;
    }

    public string? ResolveModelName(string? partNumber)
    {
        var normalized = partNumber?.Trim() ?? string.Empty;
        return PartModels.TryGetValue(normalized, out var modelName)
            ? modelName
            : null;
    }

    private static string BuildDeviceKey(string? cageCode, string? serialNumber)
    {
        var cage = cageCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var serial = serialNumber?.Trim().ToUpperInvariant() ?? string.Empty;
        return cage.Length == 0 || serial.Length == 0
            ? string.Empty
            : $"{cage}\u001f{serial}";
    }
}
