using System.IO;

namespace EquipmentTracking.App.Infrastructure;

public sealed class AppPaths
{
    public AppPaths(string? baseDataDirectory = null)
    {
        BaseDataDirectory = string.IsNullOrWhiteSpace(baseDataDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "58SOW",
                "EquipmentTrackingPlatform")
            : Path.GetFullPath(baseDataDirectory);

        SettingsPath = Path.Combine(BaseDataDirectory, "settings.json");
        DatabasePath = Path.Combine(BaseDataDirectory, "equipment-tracking.db");
        LogDirectory = Path.Combine(BaseDataDirectory, "Logs");
        WorkingDirectory = Path.Combine(BaseDataDirectory, "Working");
        ReportsDirectory = Path.Combine(BaseDataDirectory, "Reports");
        PrintJobsDirectory = Path.Combine(BaseDataDirectory, "PrintJobs");
        BackupDirectory = Path.Combine(BaseDataDirectory, "Backups");
        RecoveryDirectory = Path.Combine(BaseDataDirectory, "Recovery");
        DiagnosticsDirectory = Path.Combine(BaseDataDirectory, "Diagnostics");
        QuarantineDirectory = Path.Combine(BaseDataDirectory, "Quarantine");
        PendingRestoreDirectory = Path.Combine(BaseDataDirectory, "PendingRestore");
        PendingRestoreMarkerPath = Path.Combine(PendingRestoreDirectory, "restore-pending.json");
        BackupScheduleStatePath = Path.Combine(BaseDataDirectory, "backup-schedule-state.json");
    }

    public string BaseDataDirectory { get; }
    public string SettingsPath { get; }
    public string DatabasePath { get; }
    public string LogDirectory { get; }
    public string WorkingDirectory { get; }
    public string ReportsDirectory { get; }
    public string PrintJobsDirectory { get; }
    public string BackupDirectory { get; }
    public string RecoveryDirectory { get; }
    public string DiagnosticsDirectory { get; }
    public string QuarantineDirectory { get; }
    public string PendingRestoreDirectory { get; }
    public string PendingRestoreMarkerPath { get; }
    public string BackupScheduleStatePath { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(BaseDataDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(WorkingDirectory);
        Directory.CreateDirectory(ReportsDirectory);
        Directory.CreateDirectory(PrintJobsDirectory);
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(RecoveryDirectory);
        Directory.CreateDirectory(DiagnosticsDirectory);
        Directory.CreateDirectory(QuarantineDirectory);
        Directory.CreateDirectory(PendingRestoreDirectory);
    }
}
