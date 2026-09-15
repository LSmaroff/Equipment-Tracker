using EquipmentTracking.App.Models;

namespace EquipmentTracking.App.Services;

public sealed class DeviceRecognitionService
{
    private readonly IDeviceRecognitionDataSource _dataSource;
    private readonly BarcodeParser _barcodeParser;
    private readonly KnownDeviceCatalog _knownDevices;

    public DeviceRecognitionService(
        IDeviceRecognitionDataSource dataSource,
        BarcodeParser barcodeParser,
        KnownDeviceCatalog knownDevices)
    {
        _dataSource = dataSource;
        _barcodeParser = barcodeParser;
        _knownDevices = knownDevices;
    }

    public async Task<DeviceRecognitionResult> RecognizeAsync(
        BarcodeParseResult parsedScan,
        string? existingPartNumber = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parsedScan);

        var partNumber = FirstNonEmpty(parsedScan.PartNumber, existingPartNumber);
        var modelName = string.Empty;
        var source = string.Empty;

        if (partNumber.Length == 0 &&
            _knownDevices.TryResolveExactDevice(
                parsedScan.CageCode,
                parsedScan.SerialNumber,
                out var knownDevice))
        {
            partNumber = knownDevice.PartNumber;
            modelName = knownDevice.ModelName;
            source = knownDevice.Source;
        }

        if (partNumber.Length == 0 && parsedScan.SerialNumber.Trim().Length > 0)
        {
            var history = await _dataSource.GetDeviceIdentityHistoryAsync(
                parsedScan.SerialNumber,
                cancellationToken);
            var historicalIdentity = ResolveUnambiguousHistory(
                history,
                parsedScan.CageCode);
            if (historicalIdentity is not null)
            {
                partNumber = historicalIdentity.PartNumber;
                modelName = historicalIdentity.ModelName;
                source = "Exact serial history";
            }
        }

        if (partNumber.Length == 0)
        {
            return new DeviceRecognitionResult();
        }

        var operatorModelName = await _dataSource.ResolveModelNameAsync(
            partNumber,
            cancellationToken);
        modelName = FirstNonEmpty(
            operatorModelName,
            modelName,
            _knownDevices.ResolveModelName(partNumber));

        return new DeviceRecognitionResult
        {
            PartNumber = partNumber,
            ModelName = modelName,
            Source = operatorModelName is not null
                ? "Operator model catalog"
                : source
        };
    }

    private DeviceRecognitionResult? ResolveUnambiguousHistory(
        IReadOnlyList<DeviceIdentityHistoryItem> history,
        string? currentCageCode)
    {
        var usable = history
            .Where(item => !string.IsNullOrWhiteSpace(item.PartNumber))
            .ToArray();
        if (usable.Length == 0)
        {
            return null;
        }

        var partNumbers = usable
            .Select(item => item.PartNumber.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var modelNames = usable
            .Select(item => item.ModelName.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (partNumbers.Length != 1 || modelNames.Length > 1)
        {
            return null;
        }

        var historicalCages = usable
            .Select(item => _barcodeParser.Parse(item.RawScanValue).CageCode.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var cage = currentCageCode?.Trim() ?? string.Empty;
        if (historicalCages.Length > 1 ||
            (cage.Length > 0 &&
             historicalCages.Length == 1 &&
             !string.Equals(cage, historicalCages[0], StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return new DeviceRecognitionResult
        {
            PartNumber = partNumbers[0],
            ModelName = modelNames.FirstOrDefault() ?? string.Empty
        };
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
        ?? string.Empty;
}
