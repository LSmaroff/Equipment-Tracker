namespace EquipmentTracking.App.Models;

public sealed class DatabaseSnapshot
{
    public List<EquipmentTransaction> Transactions { get; init; } = [];
    public List<AuditRecord> AuditRecords { get; init; } = [];
}
