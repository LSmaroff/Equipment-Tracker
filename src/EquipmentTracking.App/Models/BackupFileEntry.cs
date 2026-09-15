namespace EquipmentTracking.App.Models;

public sealed class BackupFileEntry
{
    public string RelativePath { get; init; } = string.Empty;
    public string ArchiveEntryName { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTimeOffset LastWriteTime { get; init; }
}
