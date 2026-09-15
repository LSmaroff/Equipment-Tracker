namespace EquipmentTracking.App.Models;

public sealed class PickupReceipt
{
    public string Id { get; init; } = string.Empty;
    public string TransactionId { get; init; } = string.Empty;
    public int SequenceNumber { get; init; }
    public string PdfPath { get; init; } = string.Empty;
    public string Technician { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public DateTimeOffset PickedUpAt { get; init; } = DateTimeOffset.Now;
    public string Sha256 { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string SignerName { get; init; } = string.Empty;
    public string CertificateSubject { get; init; } = string.Empty;
    public string CertificateThumbprint { get; init; } = string.Empty;
    public DateTimeOffset? SignatureTime { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public IReadOnlyList<long> DeviceIds { get; init; } = [];
}
