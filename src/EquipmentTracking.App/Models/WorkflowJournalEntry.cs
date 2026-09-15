namespace EquipmentTracking.App.Models;

public sealed class WorkflowJournalEntry
{
    public string OperationId { get; set; } = Guid.NewGuid().ToString("N");
    public string OperationType { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public string Stage { get; set; } = "Created";
    public string Status { get; set; } = "Pending";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public string SourcePdfPath { get; set; } = string.Empty;
    public string DestinationPdfPath { get; set; } = string.Empty;
    public string OriginalSignedPdfPath { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public string DestinationSha256 { get; set; } = string.Empty;
    public string LastError { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public PendingIntakePayload? Intake { get; set; }
    public PendingCloseoutPayload? Closeout { get; set; }
    public PendingPartialPickupPayload? PartialPickup { get; set; }

    public string DisplayName =>
        string.Equals(OperationType, "Closeout", StringComparison.OrdinalIgnoreCase)
            ? $"Closeout {TransactionId}"
            : string.Equals(OperationType, "PartialPickup", StringComparison.OrdinalIgnoreCase)
                ? $"Partial pickup {TransactionId}"
                : $"Intake {TransactionId}";
}

public sealed class PendingIntakePayload
{
    public WorkingTransaction Working { get; set; } = new();
    public CustomerIdentity Customer { get; set; } = new();
    public string PhoneNumber { get; set; } = string.Empty;
    public string Technician { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string TicketNumber { get; set; } = string.Empty;
    public List<DeviceRecord> Devices { get; set; } = [];
}

public sealed class PendingCloseoutPayload
{
    public string TransactionId { get; set; } = string.Empty;
    public string Technician { get; set; } = string.Empty;
    public DateTimeOffset ClosedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset SignatureSearchStartedAt { get; set; } = DateTimeOffset.Now;
    public List<string> ExistingSignatureFingerprints { get; set; } = [];
}

public sealed class PendingPartialPickupPayload
{
    public string PickupId { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public List<long> DeviceIds { get; set; } = [];
    public int SequenceNumber { get; set; }
    public string Technician { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset PickedUpAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset SignatureSearchStartedAt { get; set; } = DateTimeOffset.Now;
    public List<string> ExistingSignatureFingerprints { get; set; } = [];
}
