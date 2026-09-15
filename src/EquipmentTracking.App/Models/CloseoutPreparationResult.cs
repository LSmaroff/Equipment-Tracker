namespace EquipmentTracking.App.Models;

public sealed class CloseoutPreparationResult
{
    public required EquipmentTransaction Transaction { get; init; }
    public DateTimeOffset SignatureSearchStartedAt { get; init; }
    public IReadOnlyCollection<string> ExistingSignatureFingerprints { get; init; } = [];
    public required WorkflowJournalEntry Journal { get; init; }
}
