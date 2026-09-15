using System.Globalization;
using System.IO;
using System.Text.Json;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;

namespace EquipmentTracking.App.Services;

public sealed class ScheduledBackupService : IDisposable
{
    public static readonly TimeSpan MondayFullBackupTime = new(9, 0, 0);
    public static readonly TimeSpan WeekdayDifferentialBackupTime = new(16, 0, 0);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;
    private readonly SettingsService _settings;
    private readonly DatabaseService _database;
    private readonly BackupService _backups;
    private readonly StatusService _status;
    private readonly FileLogger _logger;
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _worker;
    private BackupScheduleState _state = new();
    private DateTimeOffset _nextAutomaticRetryAt = DateTimeOffset.MinValue;
    private string _lastVerifiedFullPath = string.Empty;
    private string _lastVerifiedFullId = string.Empty;
    private string _lastVerifiedFullCompletedPdfRoot = string.Empty;
    private long _lastVerifiedFullLength = -1;
    private DateTime _lastVerifiedFullWriteTimeUtc = DateTime.MinValue;
    private DateTimeOffset _lastFullVerificationAt = DateTimeOffset.MinValue;
    private bool _disposed;

    public event EventHandler? StatusChanged;

    public ScheduledBackupService(
        AppPaths paths,
        SettingsService settings,
        DatabaseService database,
        BackupService backups,
        StatusService status,
        FileLogger logger)
    {
        _paths = paths;
        _settings = settings;
        _database = database;
        _backups = backups;
        _status = status;
        _logger = logger;
    }

    public string ScheduleDescription =>
        "Full backup Monday at 09:00; differential backup Monday through Friday at 16:00.";

    public string StatusSummary
    {
        get
        {
            if (!_settings.Current.ScheduledBackupsEnabled)
            {
                return "Scheduled backups are disabled.";
            }
            if (_state.LastFailureAt.HasValue &&
                (!_state.LastSuccessfulBackupAt.HasValue ||
                 _state.LastFailureAt > _state.LastSuccessfulBackupAt))
            {
                return $"Last scheduled backup failed {_state.LastFailureAt.Value.LocalDateTime:g}: " +
                       _state.LastFailure;
            }
            if (_state.LastSuccessfulBackupAt.HasValue)
            {
                return $"Last successful {_state.LastSuccessfulBackupType.ToLowerInvariant()} backup: " +
                       $"{_state.LastSuccessfulBackupAt.Value.LocalDateTime:g}.";
            }
            if (_state.LastFailureAt.HasValue)
            {
                return $"No scheduled backup has completed. Last attempt failed " +
                       $"{_state.LastFailureAt.Value.LocalDateTime:g}.";
            }
            return "No scheduled backup has completed yet.";
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_worker is not null)
        {
            return;
        }

        _state = LoadState();
        _worker = Task.Run(() => RunLoopAsync(_shutdown.Token));
    }

    public async Task RunDueBackupsAsync(
        DateTimeOffset? currentTime = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_settings.Current.ScheduledBackupsEnabled)
        {
            return;
        }

        await _runGate.WaitAsync(cancellationToken);
        try
        {
            var now = currentTime ?? DateTimeOffset.Now;
            await RunDueBackupsCoreAsync(now, cancellationToken);
        }
        finally
        {
            _runGate.Release();
        }
    }

    public async Task<BackupCreationResult> CreateFullBackupNowAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _runGate.WaitAsync(cancellationToken);
        try
        {
            var configuration = CaptureRunConfiguration();
            var result = await CreateFullBackupCoreAsync(configuration, cancellationToken);
            var now = DateTimeOffset.Now;
            var weekMonday = GetWeekMonday(now.Date);
            var fullDueAt = new DateTimeOffset(weekMonday + MondayFullBackupTime, now.Offset);
            if (now >= fullDueAt)
            {
                _state = new BackupScheduleState
                {
                    WeekMonday = weekMonday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    FullBackupId = result.BackupId,
                    FullBackupPath = result.BackupPath,
                    FullBackupCreatedAt = now,
                    LastSuccessfulBackupAt = now,
                    LastSuccessfulBackupType = BackupManifest.FullBackupType
                };
                var latestDifferential = GetLatestDueDifferentialDate(now, weekMonday);
                if (latestDifferential.HasValue)
                {
                    _state.CompletedDifferentialDates.Add(
                        latestDifferential.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                }
                SaveState(_state);
                OnStatusChanged();
                await _backups.ApplyScheduledRetentionAsync(
                    configuration.BackupDirectory,
                    result.BackupId,
                    configuration.MaximumFullBackupCount,
                    cancellationToken);
                _state.RetentionAppliedForFullId = result.BackupId;
            }
            _state.LastSuccessfulBackupAt = now;
            _state.LastSuccessfulBackupType = BackupManifest.FullBackupType;
            _state.LastFailure = string.Empty;
            _state.LastFailureAt = null;
            SaveState(_state);
            OnStatusChanged();
            _status.Message = $"Verified full backup completed: {result.BackupPath}";
            return result;
        }
        finally
        {
            _runGate.Release();
        }
    }

    public string ResolveBackupDirectory() =>
        _settings.ResolvePath(_settings.Current.ScheduledBackupFolder);

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await TryRunAutomaticallyAsync(cancellationToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await TryRunAutomaticallyAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Error("The scheduled-backup worker stopped unexpectedly.", ex);
        }
    }

    private async Task TryRunAutomaticallyAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        if (now < _nextAutomaticRetryAt)
        {
            return;
        }

        try
        {
            await RunDueBackupsAsync(now, cancellationToken);
            _nextAutomaticRetryAt = DateTimeOffset.MinValue;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _state.LastFailure = Limit(ex.Message, 500);
            _state.LastFailureAt = now;
            SaveState(_state);
            OnStatusChanged();
            _nextAutomaticRetryAt = now.AddMinutes(15);
            _status.Message =
                "Scheduled backup failed. The application will retry in 15 minutes while it remains open.";
            _logger.Error(
                "A scheduled backup did not complete. The application will retry in 15 minutes.",
                ex);
        }
    }

    private async Task RunDueBackupsCoreAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var configuration = CaptureRunConfiguration();
        var weekMonday = GetWeekMonday(now.Date);
        var fullDueAt = new DateTimeOffset(
            weekMonday + MondayFullBackupTime,
            now.Offset);
        if (now < fullDueAt)
        {
            var previousWeekMonday = weekMonday.AddDays(-7);
            var previousMondayText = previousWeekMonday.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);
            var previousFullIsUsable =
                string.Equals(_state.WeekMonday, previousMondayText, StringComparison.Ordinal) &&
                await StateFullBackupIsUsableAsync(configuration, cancellationToken);
            if (previousFullIsUsable)
            {
                await ApplyRetentionIfNeededAsync(configuration, cancellationToken);
                await CreateLatestDueDifferentialIfNeededAsync(
                    now,
                    previousWeekMonday,
                    configuration,
                    cancellationToken);
            }
            return;
        }

        var mondayText = weekMonday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var currentFullIsUsable =
            string.Equals(_state.WeekMonday, mondayText, StringComparison.Ordinal) &&
            await StateFullBackupIsUsableAsync(configuration, cancellationToken);

        if (!currentFullIsUsable)
        {
            var full = await CreateFullBackupCoreAsync(configuration, cancellationToken);
            _state = new BackupScheduleState
            {
                WeekMonday = mondayText,
                FullBackupId = full.BackupId,
                FullBackupPath = full.BackupPath,
                FullBackupCreatedAt = now,
                LastSuccessfulBackupAt = now,
                LastSuccessfulBackupType = BackupManifest.FullBackupType
            };
            var latestCoveredDifferential = GetLatestDueDifferentialDate(now, weekMonday);
            if (latestCoveredDifferential.HasValue)
            {
                _state.CompletedDifferentialDates.Add(
                    latestCoveredDifferential.Value.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture));
            }
            SaveState(_state);
            OnStatusChanged();
            _status.Message = $"Scheduled full backup completed: {full.BackupPath}";
        }

        await ApplyRetentionIfNeededAsync(configuration, cancellationToken);
        await CreateLatestDueDifferentialIfNeededAsync(
            now,
            weekMonday,
            configuration,
            cancellationToken);
    }

    private async Task ApplyRetentionIfNeededAsync(
        BackupRunConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (string.Equals(
                _state.RetentionAppliedForFullId,
                _state.FullBackupId,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // If a removable/network destination was temporarily unavailable after
        // the full completed, the next retry still performs retention without
        // creating another full.
        await _backups.ApplyScheduledRetentionAsync(
            configuration.BackupDirectory,
            _state.FullBackupId,
            configuration.MaximumFullBackupCount,
            cancellationToken);
        _state.RetentionAppliedForFullId = _state.FullBackupId;
        SaveState(_state);
    }

    private async Task CreateLatestDueDifferentialIfNeededAsync(
        DateTimeOffset now,
        DateTime weekMonday,
        BackupRunConfiguration configuration,
        CancellationToken cancellationToken)
    {

        var differentialDate = GetLatestDueDifferentialDate(now, weekMonday);
        if (!differentialDate.HasValue)
        {
            return;
        }

        var differentialDateText = differentialDate.Value.ToString(
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture);
        if (_state.CompletedDifferentialDates.Contains(
                differentialDateText,
                StringComparer.Ordinal))
        {
            return;
        }

        var schemaVersion = await _database.GetCurrentSchemaVersionAsync(cancellationToken);
        var differential = await _backups.CreateScheduledDifferentialBackupAsync(
            configuration.BackupDirectory,
            configuration.CompletedPdfRoot,
            schemaVersion,
            _state.FullBackupPath,
            cancellationToken);
        _state.CompletedDifferentialDates.Add(differentialDateText);
        _state.CompletedDifferentialDates = _state.CompletedDifferentialDates
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        _state.LastSuccessfulBackupAt = now;
        _state.LastSuccessfulBackupType = BackupManifest.DifferentialBackupType;
        _state.LastFailure = string.Empty;
        _state.LastFailureAt = null;
        SaveState(_state);
        OnStatusChanged();
        _status.Message = $"Scheduled differential backup completed: {differential.BackupPath}";
        _logger.Information(
            $"Completed scheduled differential backup for {differentialDateText}: {differential.BackupPath}");
    }

    private async Task<BackupCreationResult> CreateFullBackupCoreAsync(
        BackupRunConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var schemaVersion = await _database.GetCurrentSchemaVersionAsync(cancellationToken);
        var result = await _backups.CreateScheduledFullBackupAsync(
            configuration.BackupDirectory,
            configuration.CompletedPdfRoot,
            schemaVersion,
            cancellationToken);
        _logger.Information($"Completed scheduled full backup: {result.BackupPath}");
        return result;
    }

    private async Task<bool> StateFullBackupIsUsableAsync(
        BackupRunConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_state.FullBackupId) ||
            string.IsNullOrWhiteSpace(_state.FullBackupPath) ||
            !File.Exists(_state.FullBackupPath))
        {
            return false;
        }

        try
        {
            var info = new FileInfo(_state.FullBackupPath);
            var verificationAge = DateTimeOffset.UtcNow - _lastFullVerificationAt;
            var verificationIsCurrent = string.Equals(
                    _lastVerifiedFullPath,
                    info.FullName,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    _lastVerifiedFullId,
                    _state.FullBackupId,
                    StringComparison.OrdinalIgnoreCase) &&
                PathsEqual(
                    _lastVerifiedFullCompletedPdfRoot,
                    configuration.CompletedPdfRoot) &&
                PathsEqual(
                    Path.GetDirectoryName(info.FullName)!,
                    configuration.BackupDirectory) &&
                _lastVerifiedFullLength == info.Length &&
                _lastVerifiedFullWriteTimeUtc == info.LastWriteTimeUtc &&
                verificationAge >= TimeSpan.Zero &&
                verificationAge < TimeSpan.FromHours(24);
            if (verificationIsCurrent)
            {
                return true;
            }

            var verified = await _backups.VerifyBackupAsync(
                _state.FullBackupPath,
                cancellationToken);
            var metadataMatches = string.Equals(
                    verified.BackupType,
                    BackupManifest.FullBackupType,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    verified.BackupId,
                    _state.FullBackupId,
                    StringComparison.OrdinalIgnoreCase) &&
                PathsEqual(
                    Path.GetDirectoryName(info.FullName)!,
                    configuration.BackupDirectory) &&
                PathsEqual(
                    verified.CompletedPdfRoot,
                    configuration.CompletedPdfRoot);
            if (!metadataMatches)
            {
                return false;
            }

            info.Refresh();
            _lastVerifiedFullPath = info.FullName;
            _lastVerifiedFullId = verified.BackupId;
            _lastVerifiedFullCompletedPdfRoot = verified.CompletedPdfRoot;
            _lastVerifiedFullLength = info.Length;
            _lastVerifiedFullWriteTimeUtc = info.LastWriteTimeUtc;
            _lastFullVerificationAt = DateTimeOffset.UtcNow;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"The recorded Monday full backup is not usable: {ex.Message}");
            return false;
        }
    }

    private BackupRunConfiguration CaptureRunConfiguration()
    {
        var settings = _settings.Current.Clone();
        var backupDirectory = _settings.ResolvePath(settings.ScheduledBackupFolder);
        if (string.IsNullOrWhiteSpace(backupDirectory))
        {
            throw new InvalidOperationException("The scheduled-backup folder is not configured.");
        }
        var completedPdfRoot = _settings.ResolvePath(settings.CompletedPdfFolder);
        if (string.IsNullOrWhiteSpace(completedPdfRoot))
        {
            throw new InvalidOperationException("The completed-PDF folder is not configured.");
        }

        return new BackupRunConfiguration(
            Path.GetFullPath(backupDirectory),
            Path.GetFullPath(completedPdfRoot),
            Math.Clamp(settings.BackupRetentionCount, 1, 100));
    }

    private BackupScheduleState LoadState()
    {
        try
        {
            if (!File.Exists(_paths.BackupScheduleStatePath))
            {
                return new BackupScheduleState();
            }
            if (new FileInfo(_paths.BackupScheduleStatePath).Length > 1024 * 1024)
            {
                throw new InvalidDataException("The backup schedule state exceeds 1 MB.");
            }

            using var stream = File.OpenRead(_paths.BackupScheduleStatePath);
            var state = JsonSerializer.Deserialize<BackupScheduleState>(stream, JsonOptions)
                ?? new BackupScheduleState();
            state.WeekMonday ??= string.Empty;
            state.FullBackupId ??= string.Empty;
            state.FullBackupPath ??= string.Empty;
            state.RetentionAppliedForFullId ??= string.Empty;
            state.CompletedDifferentialDates ??= [];
            state.CompletedDifferentialDates = state.CompletedDifferentialDates
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .Take(366)
                .ToList();
            state.LastSuccessfulBackupType ??= string.Empty;
            state.LastFailure ??= string.Empty;
            return state;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Backup schedule state could not be loaded and will be rebuilt: {ex.Message}");
            return new BackupScheduleState();
        }
    }

    private void SaveState(BackupScheduleState state)
    {
        _paths.EnsureDirectories();
        var temporaryPath = _paths.BackupScheduleStatePath + ".tmp";
        try
        {
            using (var stream = File.Create(temporaryPath))
            {
                JsonSerializer.Serialize(stream, state, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _paths.BackupScheduleStatePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
            }
        }
    }

    private static DateTime GetWeekMonday(DateTime date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.Date.AddDays(-daysSinceMonday);
    }

    private static DateTime? GetLatestDueDifferentialDate(
        DateTimeOffset now,
        DateTime weekMonday)
    {
        var candidate = now.Date;
        if (now.TimeOfDay < WeekdayDifferentialBackupTime)
        {
            candidate = candidate.AddDays(-1);
        }

        while (candidate >= weekMonday)
        {
            if (candidate.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday)
            {
                return candidate;
            }
            candidate = candidate.AddDays(-1);
        }
        return null;
    }

    private static string Limit(string value, int maximumLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maximumLength
            ? value
            : value[..maximumLength];

    private void OnStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    private static bool PathsEqual(string first, string second) =>
        !string.IsNullOrWhiteSpace(first) &&
        !string.IsNullOrWhiteSpace(second) &&
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            StringComparison.OrdinalIgnoreCase);

    private sealed record BackupRunConfiguration(
        string BackupDirectory,
        string CompletedPdfRoot,
        int MaximumFullBackupCount);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _shutdown.Cancel();
        _shutdown.Dispose();
        GC.SuppressFinalize(this);
    }
}
