namespace EquipmentTracking.App.Models;

public sealed class PartialPickupPreparationResult
{
    public string OperationId { get; init; } = string.Empty;
    public string PreparedPdfPath { get; init; } = string.Empty;
    public required EquipmentTransaction Transaction { get; init; }
    public int SequenceNumber { get; init; }
    public IReadOnlyList<long> DeviceIds { get; init; } = [];
    public required WorkflowJournalEntry Journal { get; init; }
}
