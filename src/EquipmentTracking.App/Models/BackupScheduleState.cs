namespace EquipmentTracking.App.Models;

public sealed class BackupScheduleState
{
    public string WeekMonday { get; set; } = string.Empty;
    public string FullBackupId { get; set; } = string.Empty;
    public string FullBackupPath { get; set; } = string.Empty;
    public DateTimeOffset? FullBackupCreatedAt { get; set; }
    public string RetentionAppliedForFullId { get; set; } = string.Empty;
    public List<string> CompletedDifferentialDates { get; set; } = [];
    public DateTimeOffset? LastSuccessfulBackupAt { get; set; }
    public string LastSuccessfulBackupType { get; set; } = string.Empty;
    public string LastFailure { get; set; } = string.Empty;
    public DateTimeOffset? LastFailureAt { get; set; }
}
