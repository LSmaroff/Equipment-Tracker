namespace EquipmentTracking.App.Models;

public sealed class FileArtifactRecord
{
    public long Id { get; init; }
    public string TransactionId { get; init; } = string.Empty;
    public string ArtifactType { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
