namespace EquipmentTracking.App.Models;

public sealed class CloseoutRequest
{
    public required EquipmentTransaction Transaction { get; init; }
    public required string Technician { get; init; }
    public DateTimeOffset ClosedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset SignatureSearchStartedAt { get; init; } = DateTimeOffset.Now;
    public IReadOnlyCollection<string>? ExistingSignatureFingerprints { get; init; }
}
