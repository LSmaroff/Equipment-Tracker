namespace EquipmentTracking.App.Models;

public sealed class DashboardSummary
{
    public int ActiveDeviceCount { get; init; }
    public int ReturnedTodayCount { get; init; }
    public int ActiveTransactionCount { get; init; }
    public int ArchivedTransactionCount { get; init; }
    public int TotalTransactionCount => ActiveTransactionCount + ArchivedTransactionCount;
}
