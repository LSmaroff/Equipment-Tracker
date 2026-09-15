namespace EquipmentTracking.App.Models;

public sealed class AuditRecord
{
    public long Id { get; init; }
    public string TransactionId { get; init; } = string.Empty;
    public long? DeviceId { get; init; }
    public string Action { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public string Technician { get; init; } = string.Empty;
    public string ComputerName { get; init; } = string.Empty;
    public DateTimeOffset ActionTime { get; init; }
}
