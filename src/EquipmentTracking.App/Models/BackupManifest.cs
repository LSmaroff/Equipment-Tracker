namespace EquipmentTracking.App.Models;

public sealed class BackupManifest
{
    public const int CurrentFormatVersion = 3;
    public const string LegacyBackupType = "Legacy";
    public const string FullBackupType = "Full";
    public const string DifferentialBackupType = "Differential";

    public int FormatVersion { get; init; } = 1;
    public string BackupId { get; init; } = string.Empty;
    public string BackupType { get; init; } = LegacyBackupType;
    public string BaseFullBackupId { get; init; } = string.Empty;
    public string BaseFullBackupFileName { get; init; } = string.Empty;
    public string ApplicationVersion { get; init; } = string.Empty;
    public int DatabaseSchemaVersion { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public string ComputerName { get; init; } = Environment.MachineName;
    public string UserName { get; init; } = Environment.UserName;
    public string DatabaseSha256 { get; init; } = string.Empty;
    public bool IncludesSettings { get; init; }
    public string SettingsSha256 { get; init; } = string.Empty;
    public string CompletedPdfRoot { get; init; } = string.Empty;
    public List<BackupFileEntry> RecordFiles { get; init; } = [];
    public List<string> DeletedRecordPaths { get; init; } = [];
}
