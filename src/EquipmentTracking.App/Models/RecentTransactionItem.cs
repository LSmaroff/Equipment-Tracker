namespace EquipmentTracking.App.Models;

public sealed class RecentTransactionItem
{
    public string TransactionId { get; init; } = string.Empty;
    public string TicketNumber { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string Organization { get; init; } = string.Empty;
    public string Technician { get; init; } = string.Empty;
    public int DeviceCount { get; init; }
    public string StatusSummary { get; init; } = string.Empty;
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }
    public bool IsArchived { get; init; }
    public string PdfPath { get; init; } = string.Empty;
}
