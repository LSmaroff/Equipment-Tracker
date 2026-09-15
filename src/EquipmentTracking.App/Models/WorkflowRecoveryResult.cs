namespace EquipmentTracking.App.Models;

public sealed class WorkflowRecoveryResult
{
    public string OperationId { get; init; } = string.Empty;
    public string TransactionId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string PdfPath { get; init; } = string.Empty;
    public bool Completed { get; init; }
}
