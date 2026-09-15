namespace EquipmentTracking.App.Models;

public sealed class BackupCreationResult
{
    public string BackupPath { get; init; } = string.Empty;
    public string BackupId { get; init; } = string.Empty;
    public string BackupType { get; init; } = string.Empty;
    public string ArchiveSha256 { get; init; } = string.Empty;
    public int RecordFileCount { get; init; }
    public long ArchiveSizeBytes { get; init; }
}
