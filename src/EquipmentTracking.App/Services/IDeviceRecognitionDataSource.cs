using EquipmentTracking.App.Models;

namespace EquipmentTracking.App.Services;

public interface IDeviceRecognitionDataSource
{
    Task<string?> ResolveModelNameAsync(
        string? partNumber,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceIdentityHistoryItem>> GetDeviceIdentityHistoryAsync(
        string? serialNumber,
        CancellationToken cancellationToken = default);
}
