namespace EquipmentTracking.App.Models;

public sealed class PickupReceiptCommitResult
{
    public required PickupReceipt Receipt { get; init; }
    public int PickedUpCount { get; init; }
    public int RemainingDeviceCount { get; init; }
    public bool ParentArchived { get; init; }
    public bool WasAlreadyCommitted { get; init; }
}
