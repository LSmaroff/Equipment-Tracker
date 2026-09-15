namespace EquipmentTracking.App.Models;

public sealed class WorkingTransaction
{
    public string TransactionId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public string WorkingFolder { get; init; } = string.Empty;
    public string TemplateCopyPath { get; init; } = string.Empty;
    public string PreparedPdfPath { get; init; } = string.Empty;
}
