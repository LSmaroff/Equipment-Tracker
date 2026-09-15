using System.IO;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;

namespace EquipmentTracking.App.Services;

public sealed class MaintenanceService
{
    private readonly AppPaths _paths;
    private readonly BackupService _backups;
    private readonly WorkflowJournalService _journals;
    private readonly FileLogger _logger;

    public MaintenanceService(
        AppPaths paths,
        BackupService backups,
        WorkflowJournalService journals,
        FileLogger logger)
    {
        _paths = paths;
        _backups = backups;
        _journals = journals;
        _logger = logger;
    }

    public void Run(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            CleanupTemporaryFiles(settings.TemporaryFileRetentionDays);
            _journals.CleanupCompleted(settings.CompletedWorkflowRetentionDays);
            _backups.ApplyRetention(settings.BackupRetentionCount);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Startup maintenance did not complete: {ex.Message}");
        }
    }

    private void CleanupTemporaryFiles(int retentionDays)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Clamp(retentionDays, 1, 3650));
        CleanupDirectory(_paths.WorkingDirectory, cutoff, deleteDirectories: true);
        CleanupDirectory(_paths.ReportsDirectory, cutoff, deleteDirectories: false);
        CleanupDirectory(_paths.QuarantineDirectory, cutoff, deleteDirectories: false);
        CleanupDirectory(_paths.RecoveryDirectory, cutoff, deleteDirectories: false);
        CleanupDirectory(_paths.PendingRestoreDirectory, cutoff, deleteDirectories: false);
        // A print sheet is a disposable visual derivative, never an operational
        // record. The normal print flow removes it when the operator closes the
        // printing notice; startup removes any sheet abandoned by a crash or a
        // PDF viewer that held the file open past that notice.
        CleanupFiles(_paths.PrintJobsDirectory, "1297-two-copy-*.pdf", DateTime.MaxValue);
        CleanupFiles(_paths.PrintJobsDirectory, ".*.tmp.pdf", cutoff);

        CleanupFiles(_paths.BackupDirectory, ".*.tmp", cutoff);
        CleanupFiles(_paths.BackupDirectory, ".*.db", cutoff);
        CleanupRestoreStagingDirectories(cutoff);
    }

    private void CleanupRestoreStagingDirectories(DateTime cutoff)
    {
        if (!Directory.Exists(_paths.BaseDataDirectory))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(
                     _paths.BaseDataDirectory,
                     ".restore-stage-*",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(directory) < cutoff)
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch
            {
                // Best effort.
            }
        }
    }

    private static void CleanupFiles(string directory, string pattern, DateTime cutoff)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // Best effort.
            }
        }
    }

    private static void CleanupDirectory(
        string directory,
        DateTime cutoff,
        bool deleteDirectories)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.tmp", SearchOption.AllDirectories))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // Best effort.
            }
        }

        if (!deleteDirectories)
        {
            return;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            try
            {
                var latestWrite = Directory.EnumerateFiles(child, "*", SearchOption.AllDirectories)
                    .Select(File.GetLastWriteTimeUtc)
                    .DefaultIfEmpty(Directory.GetLastWriteTimeUtc(child))
                    .Max();
                if (latestWrite < cutoff && !Directory.EnumerateFileSystemEntries(child).Any())
                {
                    Directory.Delete(child, recursive: false);
                }
            }
            catch
            {
                // Best effort.
            }
        }
    }
}
