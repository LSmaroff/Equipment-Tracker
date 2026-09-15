using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using Microsoft.Data.Sqlite;

namespace EquipmentTracking.App.Services;

public sealed class BackupService
{
    private const string DatabaseEntryName = "equipment-tracking.db";
    private const string SettingsEntryName = "settings.json";
    private const string ManifestEntryName = "backup-manifest.json";
    private const string RecordsEntryPrefix = "records/";
    private const string ScheduledBackupPrefix = "EquipmentTracking-Scheduled-";
    private const long MaximumManifestBytes = 16L * 1024L * 1024L;
    private const int MaximumRecordFileCount = 100_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;
    private readonly FileLogger _logger;

    public BackupService(AppPaths paths, FileLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task ApplyPendingRestoreIfAnyAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureDirectories();
        if (!File.Exists(_paths.PendingRestoreMarkerPath))
        {
            return;
        }

        RestoreMarker? marker;
        await using (var markerStream = File.OpenRead(_paths.PendingRestoreMarkerPath))
        {
            marker = await JsonSerializer.DeserializeAsync<RestoreMarker>(
                markerStream,
                JsonOptions,
                cancellationToken);
        }

        if (marker is null || !File.Exists(marker.StagedDatabasePath))
        {
            throw new InvalidOperationException(
                "A pending database restore is incomplete. Remove the PendingRestore folder or stage the backup again.");
        }
        if (marker.RecordFiles is null)
        {
            throw new InvalidDataException("The pending restore record list is invalid.");
        }

        if (marker.DatabaseSchemaVersion > DatabaseService.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"The staged restore uses database schema version {marker.DatabaseSchemaVersion}, " +
                $"but this application supports only version {DatabaseService.CurrentSchemaVersion}.");
        }

        var stagedHash = await ComputeSha256Async(marker.StagedDatabasePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(marker.DatabaseSha256) ||
            !string.Equals(stagedHash, marker.DatabaseSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The staged database changed after the restore was prepared and failed SHA-256 verification.");
        }
        await VerifyDatabaseFileAsync(marker.StagedDatabasePath, cancellationToken);

        if (marker.RestoreSettings)
        {
            if (string.IsNullOrWhiteSpace(marker.StagedSettingsPath) ||
                !File.Exists(marker.StagedSettingsPath))
            {
                throw new InvalidDataException(
                    "The pending restore requires settings, but its staged settings file is missing.");
            }

            if (!string.IsNullOrWhiteSpace(marker.SettingsSha256))
            {
                var stagedSettingsHash = await ComputeSha256Async(
                    marker.StagedSettingsPath,
                    cancellationToken);
                if (!string.Equals(
                        stagedSettingsHash,
                        marker.SettingsSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "The staged settings changed after the restore was prepared and failed SHA-256 verification.");
                }
            }
        }

        if (marker.RecordFiles.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(marker.StagedRecordsDirectory) ||
                !Directory.Exists(marker.StagedRecordsDirectory) ||
                string.IsNullOrWhiteSpace(marker.TargetCompletedPdfRoot))
            {
                throw new InvalidDataException(
                    "The pending restore contains 1297 records, but its staged record folder or destination is missing.");
            }

            await VerifyStagedRecordFilesAsync(
                marker.StagedRecordsDirectory,
                marker.RecordFiles,
                cancellationToken);
        }

        var safetyFolder = Path.Combine(
            _paths.BackupDirectory,
            $"pre-restore-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(safetyFolder);
        CopyIfExists(_paths.DatabasePath, Path.Combine(safetyFolder, DatabaseEntryName));
        CopyIfExists(_paths.DatabasePath + "-wal", Path.Combine(safetyFolder, DatabaseEntryName + "-wal"));
        CopyIfExists(_paths.DatabasePath + "-shm", Path.Combine(safetyFolder, DatabaseEntryName + "-shm"));
        CopyIfExists(_paths.SettingsPath, Path.Combine(safetyFolder, SettingsEntryName));

        if (marker.RecordFiles.Count > 0)
        {
            await ApplyStagedRecordFilesAsync(
                marker,
                Path.Combine(safetyFolder, "records"),
                cancellationToken);
        }

        DeleteIfExists(_paths.DatabasePath + "-wal");
        DeleteIfExists(_paths.DatabasePath + "-shm");
        File.Copy(marker.StagedDatabasePath, _paths.DatabasePath, overwrite: true);

        if (marker.RestoreSettings &&
            !string.IsNullOrWhiteSpace(marker.StagedSettingsPath) &&
            File.Exists(marker.StagedSettingsPath))
        {
            File.Copy(marker.StagedSettingsPath, _paths.SettingsPath, overwrite: true);
        }

        Directory.Delete(_paths.PendingRestoreDirectory, recursive: true);
        Directory.CreateDirectory(_paths.PendingRestoreDirectory);
        _logger.Information($"Applied staged database and record restore. Safety copy: {safetyFolder}");
    }

    public async Task<string> CreateBackupAsync(
        string reason,
        int schemaVersion,
        bool includeSettings,
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureDirectories();
        if (!File.Exists(_paths.DatabasePath))
        {
            throw new FileNotFoundException("The equipment-tracking database does not exist yet.", _paths.DatabasePath);
        }

        var safeReason = SanitizeFilePart(reason);
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var finalPath = BuildUniquePath(
            _paths.BackupDirectory,
            $"EquipmentTracking-{safeReason}-{timestamp}.zip");
        var temporaryZip = Path.Combine(_paths.BackupDirectory, $".{Guid.NewGuid():N}.zip.tmp");
        var temporaryDatabase = Path.Combine(_paths.BackupDirectory, $".{Guid.NewGuid():N}.db");

        try
        {
            await CreateConsistentDatabaseCopyAsync(temporaryDatabase, cancellationToken);
            var databaseHash = await ComputeSha256Async(temporaryDatabase, cancellationToken);
            var includesSettings = includeSettings && File.Exists(_paths.SettingsPath);
            var manifest = new BackupManifest
            {
                FormatVersion = BackupManifest.CurrentFormatVersion,
                BackupId = Guid.NewGuid().ToString("N"),
                BackupType = BackupManifest.LegacyBackupType,
                ApplicationVersion = GetApplicationVersion(),
                DatabaseSchemaVersion = schemaVersion,
                CreatedAt = DateTimeOffset.Now,
                DatabaseSha256 = databaseHash,
                IncludesSettings = includesSettings,
                SettingsSha256 = includesSettings
                    ? await ComputeSha256Async(_paths.SettingsPath, cancellationToken)
                    : string.Empty
            };

            await WriteBackupArchiveAsync(
                temporaryZip,
                temporaryDatabase,
                manifest,
                [],
                cancellationToken);
            File.Move(temporaryZip, finalPath, overwrite: false);
            await VerifyBackupAsync(finalPath, cancellationToken);
            _logger.Information($"Created verified application backup: {finalPath}");
            return finalPath;
        }
        catch
        {
            DeleteIfExists(finalPath);
            throw;
        }
        finally
        {
            DeleteIfExists(temporaryDatabase);
            DeleteIfExists(temporaryZip);
        }
    }

    public Task<BackupCreationResult> CreateScheduledFullBackupAsync(
        string backupDirectory,
        string completedPdfRoot,
        int schemaVersion,
        CancellationToken cancellationToken = default)
    {
        return CreateScheduledBackupAsync(
            BackupManifest.FullBackupType,
            backupDirectory,
            completedPdfRoot,
            schemaVersion,
            baseFullBackupPath: null,
            cancellationToken);
    }

    public Task<BackupCreationResult> CreateScheduledDifferentialBackupAsync(
        string backupDirectory,
        string completedPdfRoot,
        int schemaVersion,
        string baseFullBackupPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseFullBackupPath))
        {
            throw new ArgumentException("A verified Monday full backup is required.", nameof(baseFullBackupPath));
        }

        return CreateScheduledBackupAsync(
            BackupManifest.DifferentialBackupType,
            backupDirectory,
            completedPdfRoot,
            schemaVersion,
            baseFullBackupPath,
            cancellationToken);
    }

    public async Task<BackupManifest> VerifyBackupAsync(
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("The selected backup file was not found.", backupPath);
        }

        _paths.EnsureDirectories();
        var verificationDatabase = Path.Combine(
            _paths.BaseDataDirectory,
            $".backup-verify-{Guid.NewGuid():N}.db");

        try
        {
            using var archive = ZipFile.OpenRead(backupPath);
            EnsureNoDuplicateEntries(archive);
            var manifest = await ReadManifestAsync(archive, cancellationToken);
            ValidateManifest(manifest);

            var databaseEntry = GetRequiredEntry(archive, DatabaseEntryName);
            await ExtractEntryAsync(databaseEntry, verificationDatabase, cancellationToken);
            var databaseHash = await ComputeSha256Async(verificationDatabase, cancellationToken);
            if (!string.Equals(databaseHash, manifest.DatabaseSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The database in the backup failed SHA-256 verification.");
            }
            await VerifyDatabaseFileAsync(verificationDatabase, cancellationToken);

            if (manifest.IncludesSettings && manifest.FormatVersion >= 2)
            {
                var settingsEntry = GetRequiredEntry(archive, SettingsEntryName);
                var settingsHash = await ComputeSha256Async(settingsEntry, cancellationToken);
                if (!string.Equals(settingsHash, manifest.SettingsSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The settings in the backup failed SHA-256 verification.");
                }
            }

            foreach (var file in manifest.RecordFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateRecordEntry(file);
                var entry = GetRequiredEntry(archive, file.ArchiveEntryName);
                if (entry.Length != file.SizeBytes)
                {
                    throw new InvalidDataException(
                        $"The backup record '{file.RelativePath}' has an unexpected size.");
                }

                var hash = await ComputeSha256Async(entry, cancellationToken);
                if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"The backup record '{file.RelativePath}' failed SHA-256 verification.");
                }
            }

            return manifest;
        }
        finally
        {
            DeleteIfExists(verificationDatabase);
        }
    }

    public async Task<BackupManifest> GetManifestAsync(
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("The selected backup file was not found.", backupPath);
        }

        using var archive = ZipFile.OpenRead(backupPath);
        EnsureNoDuplicateEntries(archive);
        var manifest = await ReadManifestAsync(archive, cancellationToken);
        ValidateManifest(manifest);
        return manifest;
    }

    public async Task StageRestoreAsync(
        string backupPath,
        bool restoreSettings,
        CancellationToken cancellationToken = default)
    {
        await StageRestoreCoreAsync(
            backupPath,
            restoreSettings,
            targetCompletedPdfRoot: null,
            cancellationToken);
    }

    public async Task StageRestoreAsync(
        string backupPath,
        bool restoreSettings,
        string targetCompletedPdfRoot,
        CancellationToken cancellationToken = default)
    {
        await StageRestoreCoreAsync(
            backupPath,
            restoreSettings,
            targetCompletedPdfRoot,
            cancellationToken);
    }

    public void ApplyRetention(int maximumBackupCount)
    {
        _paths.EnsureDirectories();
        var keep = Math.Clamp(maximumBackupCount, 1, 100);
        var backups = Directory.EnumerateFiles(_paths.BackupDirectory, "EquipmentTracking-*.zip")
            .Select(path => new FileInfo(path))
            .Where(file => !file.Name.StartsWith(ScheduledBackupPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();

        foreach (var file in backups.Skip(keep))
        {
            TryDeleteBackupAndSidecar(file.FullName);
        }
    }

    public async Task ApplyScheduledRetentionAsync(
        string backupDirectory,
        string currentFullBackupId,
        int maximumFullBackupCount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentFullBackupId))
        {
            throw new ArgumentException("The current verified full backup ID is required.", nameof(currentFullBackupId));
        }

        var directory = Path.GetFullPath(backupDirectory);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                "The scheduled backup destination is unavailable. No prior backup was removed: " + directory);
        }

        var discovered = new List<(string Path, BackupManifest Manifest)>();
        foreach (var path in Directory.EnumerateFiles(
                     directory,
                     ScheduledBackupPrefix + "*.zip",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                discovered.Add((path, await GetManifestAsync(path, cancellationToken)));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Scheduled backup retention preserved an unreadable archive '{path}': {ex.Message}");
            }
        }

        var currentFull = discovered.FirstOrDefault(item =>
            string.Equals(
                item.Manifest.BackupType,
                BackupManifest.FullBackupType,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                item.Manifest.BackupId,
                currentFullBackupId,
                StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(currentFull.Path))
        {
            throw new InvalidDataException(
                "Scheduled backup retention could not find the new Monday full backup. No prior backup was removed.");
        }

        var verifiedCurrentFull = await VerifyBackupAsync(currentFull.Path, cancellationToken);
        if (!string.Equals(
                verifiedCurrentFull.BackupType,
                BackupManifest.FullBackupType,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                verifiedCurrentFull.BackupId,
                currentFullBackupId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The new Monday full backup did not pass retention verification. No prior backup was removed.");
        }

        foreach (var item in discovered.Where(item =>
                     string.Equals(
                         item.Manifest.BackupType,
                         BackupManifest.DifferentialBackupType,
                         StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(
                         item.Manifest.BaseFullBackupId,
                         currentFullBackupId,
                         StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                await VerifyBackupAsync(item.Path, cancellationToken);
                TryDeleteBackupAndSidecar(item.Path);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    $"Scheduled backup retention preserved a corrupt differential archive '{item.Path}': {ex.Message}");
            }
        }

        var keep = Math.Clamp(maximumFullBackupCount, 1, 100);
        var fullBackups = discovered
            .Where(item => string.Equals(
                item.Manifest.BackupType,
                BackupManifest.FullBackupType,
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Manifest.CreatedAt)
            .ToArray();

        foreach (var item in fullBackups.Skip(keep))
        {
            if (!string.Equals(item.Manifest.BackupId, currentFullBackupId, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await VerifyBackupAsync(item.Path, cancellationToken);
                    TryDeleteBackupAndSidecar(item.Path);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warning(
                        $"Scheduled backup retention preserved a corrupt full archive '{item.Path}': {ex.Message}");
                }
            }
        }
    }

    private async Task<BackupCreationResult> CreateScheduledBackupAsync(
        string backupType,
        string backupDirectory,
        string completedPdfRoot,
        int schemaVersion,
        string? baseFullBackupPath,
        CancellationToken cancellationToken)
    {
        _paths.EnsureDirectories();
        if (!File.Exists(_paths.DatabasePath))
        {
            throw new FileNotFoundException("The equipment-tracking database does not exist yet.", _paths.DatabasePath);
        }

        var destination = Path.GetFullPath(backupDirectory);
        var recordsRoot = Path.GetFullPath(completedPdfRoot);
        if (PathsOverlap(destination, recordsRoot) ||
            PathsOverlap(destination, _paths.BaseDataDirectory))
        {
            throw new InvalidOperationException(
                "The scheduled backup folder must be separate from both the application-data and completed-PDF folders.");
        }

        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(recordsRoot);

        BackupManifest? baseManifest = null;
        if (string.Equals(backupType, BackupManifest.DifferentialBackupType, StringComparison.Ordinal))
        {
            if (!PathsEqual(Path.GetDirectoryName(Path.GetFullPath(baseFullBackupPath!))!, destination))
            {
                throw new InvalidDataException(
                    "The Monday full backup must be in the same folder as its differential backups.");
            }
            baseManifest = await VerifyBackupAsync(baseFullBackupPath!, cancellationToken);
            if (!string.Equals(baseManifest.BackupType, BackupManifest.FullBackupType, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The selected differential base is not a full backup.");
            }
            if (!PathsEqual(baseManifest.CompletedPdfRoot, recordsRoot))
            {
                throw new InvalidDataException(
                    "The completed-PDF folder changed after the Monday full backup. Create a new full backup before creating differentials.");
            }
        }

        var backupId = Guid.NewGuid().ToString("N");
        var createdAt = DateTimeOffset.Now;
        var timestamp = createdAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var finalPath = BuildUniquePath(
            destination,
            $"{ScheduledBackupPrefix}{backupType}-{timestamp}-{backupId[..8]}.zip");
        var temporaryZip = Path.Combine(destination, $".{Guid.NewGuid():N}.zip.tmp");
        var temporaryDatabase = Path.Combine(destination, $".{Guid.NewGuid():N}.db");

        try
        {
            await CreateConsistentDatabaseCopyAsync(temporaryDatabase, cancellationToken);
            var protectedRecordPaths = await ValidateProtectedRecordPathsAsync(
                temporaryDatabase,
                recordsRoot,
                cancellationToken);
            var databaseHash = await ComputeSha256Async(temporaryDatabase, cancellationToken);
            var settingsIncluded = File.Exists(_paths.SettingsPath);
            var settingsHash = settingsIncluded
                ? await ComputeSha256Async(_paths.SettingsPath, cancellationToken)
                : string.Empty;

            var enumerationOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                ReturnSpecialDirectories = false,
                MatchCasing = MatchCasing.CaseInsensitive,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            var candidateFiles = Directory
                .EnumerateFiles(recordsRoot, "*.pdf", enumerationOptions)
                .Select(path => new SourceRecordFile(
                    path,
                    NormalizeRelativePath(recordsRoot, path),
                    File.GetLastWriteTimeUtc(path)))
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var baseHashes = baseManifest?.RecordFiles.ToDictionary(
                    file => file.RelativePath,
                    file => file.Sha256,
                    StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var currentRelativePaths = candidateFiles
                .Select(file => file.RelativePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingProtectedPath = protectedRecordPaths.FirstOrDefault(
                path => !currentRelativePaths.Contains(path));
            if (!string.IsNullOrWhiteSpace(missingProtectedPath))
            {
                throw new IOException(
                    $"The official 1297 record '{missingProtectedPath}' changed or disappeared while the backup was being prepared. " +
                    "The backup was discarded and will be retried.");
            }
            List<string> deletedRecordPaths = baseManifest is null
                ? []
                : baseManifest.RecordFiles
                    .Select(file => file.RelativePath)
                    .Where(path => !currentRelativePaths.Contains(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            var includedFiles = new List<SourceRecordFile>();

            if (baseManifest is null)
            {
                includedFiles.AddRange(candidateFiles);
            }
            else
            {
                foreach (var file in candidateFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var currentHash = await ComputeSha256Async(file.FullPath, cancellationToken);
                    if (!baseHashes.TryGetValue(file.RelativePath, out var baseHash) ||
                        !string.Equals(currentHash, baseHash, StringComparison.OrdinalIgnoreCase))
                    {
                        includedFiles.Add(file with { ExpectedSha256 = currentHash });
                    }
                }
            }

            var recordManifestEntries = new List<BackupFileEntry>(includedFiles.Count);
            await using (var zipStream = new FileStream(
                temporaryZip,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                archive.CreateEntryFromFile(temporaryDatabase, DatabaseEntryName, CompressionLevel.Optimal);
                if (settingsIncluded)
                {
                    archive.CreateEntryFromFile(_paths.SettingsPath, SettingsEntryName, CompressionLevel.Optimal);
                }

                foreach (var file in includedFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entryName = RecordsEntryPrefix + file.RelativePath.Replace('\\', '/');
                    var copied = await WriteRecordEntryAsync(
                        archive,
                        entryName,
                        file,
                        cancellationToken);
                    recordManifestEntries.Add(copied);
                }

                var manifest = new BackupManifest
                {
                    FormatVersion = BackupManifest.CurrentFormatVersion,
                    BackupId = backupId,
                    BackupType = backupType,
                    BaseFullBackupId = baseManifest?.BackupId ?? string.Empty,
                    BaseFullBackupFileName = baseFullBackupPath is null
                        ? string.Empty
                        : Path.GetFileName(baseFullBackupPath),
                    ApplicationVersion = GetApplicationVersion(),
                    DatabaseSchemaVersion = schemaVersion,
                    CreatedAt = createdAt,
                    DatabaseSha256 = databaseHash,
                    IncludesSettings = settingsIncluded,
                    SettingsSha256 = settingsHash,
                    CompletedPdfRoot = recordsRoot,
                    RecordFiles = recordManifestEntries,
                    DeletedRecordPaths = deletedRecordPaths
                };

                var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                await using var manifestStream = manifestEntry.Open();
                await JsonSerializer.SerializeAsync(
                    manifestStream,
                    manifest,
                    JsonOptions,
                    cancellationToken);
            }

            File.Move(temporaryZip, finalPath, overwrite: false);
            var verifiedManifest = await VerifyBackupAsync(finalPath, cancellationToken);
            var archiveHash = await ComputeSha256Async(finalPath, cancellationToken);
            WriteHashSidecar(finalPath, archiveHash);
            var fileInfo = new FileInfo(finalPath);
            _logger.Information(
                $"Created and verified {backupType.ToLowerInvariant()} scheduled backup " +
                $"with {verifiedManifest.RecordFiles.Count} included record file(s) and " +
                $"{verifiedManifest.DeletedRecordPaths.Count} deletion marker(s): {finalPath}");

            return new BackupCreationResult
            {
                BackupPath = finalPath,
                BackupId = backupId,
                BackupType = backupType,
                ArchiveSha256 = archiveHash,
                RecordFileCount = verifiedManifest.RecordFiles.Count,
                ArchiveSizeBytes = fileInfo.Length
            };
        }
        catch
        {
            DeleteIfExists(finalPath);
            DeleteIfExists(finalPath + ".sha256");
            throw;
        }
        finally
        {
            DeleteIfExists(temporaryDatabase);
            DeleteIfExists(temporaryZip);
        }
    }

    private async Task StageRestoreCoreAsync(
        string backupPath,
        bool restoreSettings,
        string? targetCompletedPdfRoot,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("The selected backup file was not found.", backupPath);
        }

        _paths.EnsureDirectories();
        var selectedManifest = await VerifyBackupAsync(backupPath, cancellationToken);
        if (selectedManifest.DatabaseSchemaVersion > DatabaseService.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"This backup uses database schema version {selectedManifest.DatabaseSchemaVersion}, " +
                $"but this application supports only version {DatabaseService.CurrentSchemaVersion}.");
        }

        var temporaryStaging = Path.Combine(
            _paths.BaseDataDirectory,
            $".restore-stage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryStaging);
        var temporaryRecords = Path.Combine(temporaryStaging, "records");
        Directory.CreateDirectory(temporaryRecords);
        var committedToPending = false;

        try
        {
            var stagedDatabase = Path.Combine(temporaryStaging, DatabaseEntryName);
            var stagedSettings = Path.Combine(temporaryStaging, SettingsEntryName);
            await ExtractCoreRestoreEntriesAsync(
                backupPath,
                stagedDatabase,
                stagedSettings,
                restoreSettings,
                cancellationToken);

            var mergedRecords = new Dictionary<string, BackupFileEntry>(StringComparer.OrdinalIgnoreCase);
            BackupManifest? baseManifest = null;
            if (string.Equals(
                    selectedManifest.BackupType,
                    BackupManifest.DifferentialBackupType,
                    StringComparison.OrdinalIgnoreCase))
            {
                var basePath = await FindBaseFullBackupAsync(
                    backupPath,
                    selectedManifest,
                    cancellationToken);
                baseManifest = await VerifyBackupAsync(basePath, cancellationToken);
                await ExtractRecordFilesAsync(
                    basePath,
                    baseManifest.RecordFiles,
                    temporaryRecords,
                    overwrite: false,
                    cancellationToken);
                foreach (var record in baseManifest.RecordFiles)
                {
                    mergedRecords[record.RelativePath] = record;
                }
            }

            foreach (var deletedPath in selectedManifest.DeletedRecordPaths)
            {
                ValidateRelativeRecordPath(deletedPath);
                mergedRecords.Remove(deletedPath);
                DeleteIfExists(BuildSafeChildPath(temporaryRecords, deletedPath));
            }

            await ExtractRecordFilesAsync(
                backupPath,
                selectedManifest.RecordFiles,
                temporaryRecords,
                overwrite: true,
                cancellationToken);
            foreach (var record in selectedManifest.RecordFiles)
            {
                mergedRecords[record.RelativePath] = record;
            }

            var sourceRoot = selectedManifest.CompletedPdfRoot;
            if (string.IsNullOrWhiteSpace(sourceRoot) && baseManifest is not null)
            {
                sourceRoot = baseManifest.CompletedPdfRoot;
            }

            var targetRoot = string.IsNullOrWhiteSpace(targetCompletedPdfRoot)
                ? sourceRoot
                : Path.GetFullPath(targetCompletedPdfRoot);
            if (mergedRecords.Count > 0 && string.IsNullOrWhiteSpace(targetRoot))
            {
                throw new InvalidDataException(
                    "This backup contains 1297 records but does not identify a completed-PDF destination.");
            }

            await VerifyStagedRecordFilesAsync(
                temporaryRecords,
                mergedRecords.Values,
                cancellationToken);

            var databaseHash = await ComputeSha256Async(stagedDatabase, cancellationToken);
            if (!string.Equals(databaseHash, selectedManifest.DatabaseSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The staged database failed SHA-256 verification.");
            }
            await VerifyDatabaseFileAsync(stagedDatabase, cancellationToken);

            if (!string.IsNullOrWhiteSpace(sourceRoot) &&
                !string.IsNullOrWhiteSpace(targetRoot) &&
                !PathsEqual(sourceRoot, targetRoot))
            {
                await RemapDatabaseRecordPathsAsync(
                    stagedDatabase,
                    sourceRoot,
                    targetRoot,
                    cancellationToken);
                databaseHash = await ComputeSha256Async(stagedDatabase, cancellationToken);
                if (restoreSettings && File.Exists(stagedSettings))
                {
                    await RemapStagedSettingsAsync(stagedSettings, targetRoot, cancellationToken);
                }
            }

            if (Directory.Exists(_paths.PendingRestoreDirectory))
            {
                Directory.Delete(_paths.PendingRestoreDirectory, recursive: true);
            }
            Directory.Move(temporaryStaging, _paths.PendingRestoreDirectory);
            committedToPending = true;

            stagedDatabase = Path.Combine(_paths.PendingRestoreDirectory, DatabaseEntryName);
            stagedSettings = Path.Combine(_paths.PendingRestoreDirectory, SettingsEntryName);
            temporaryRecords = Path.Combine(_paths.PendingRestoreDirectory, "records");
            var marker = new RestoreMarker
            {
                SourceBackupPath = Path.GetFullPath(backupPath),
                StagedDatabasePath = stagedDatabase,
                StagedSettingsPath = File.Exists(stagedSettings) ? stagedSettings : string.Empty,
                RestoreSettings = restoreSettings && File.Exists(stagedSettings),
                SettingsSha256 = restoreSettings && File.Exists(stagedSettings)
                    ? await ComputeSha256Async(stagedSettings, cancellationToken)
                    : string.Empty,
                DatabaseSha256 = databaseHash,
                DatabaseSchemaVersion = selectedManifest.DatabaseSchemaVersion,
                StagedAt = DateTimeOffset.Now,
                StagedRecordsDirectory = mergedRecords.Count > 0 ? temporaryRecords : string.Empty,
                SourceCompletedPdfRoot = sourceRoot,
                TargetCompletedPdfRoot = targetRoot,
                RecordFiles = mergedRecords.Values
                    .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            await using var markerStream = File.Create(_paths.PendingRestoreMarkerPath);
            await JsonSerializer.SerializeAsync(markerStream, marker, JsonOptions, cancellationToken);
            await markerStream.FlushAsync(cancellationToken);
            markerStream.Flush(flushToDisk: true);
            _logger.Information(
                $"Staged verified restore from {backupPath} with {marker.RecordFiles.Count} record file(s). " +
                "It will be applied on the next application start.");
        }
        catch
        {
            DeleteDirectoryIfExists(
                committedToPending ? _paths.PendingRestoreDirectory : temporaryStaging);
            throw;
        }
    }

    private async Task WriteBackupArchiveAsync(
        string temporaryZip,
        string temporaryDatabase,
        BackupManifest manifest,
        IReadOnlyCollection<SourceRecordFile> records,
        CancellationToken cancellationToken)
    {
        await using var zipStream = new FileStream(
            temporaryZip,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);
        archive.CreateEntryFromFile(temporaryDatabase, DatabaseEntryName, CompressionLevel.Optimal);
        if (manifest.IncludesSettings)
        {
            archive.CreateEntryFromFile(_paths.SettingsPath, SettingsEntryName, CompressionLevel.Optimal);
        }

        foreach (var record in records)
        {
            await WriteRecordEntryAsync(
                archive,
                RecordsEntryPrefix + record.RelativePath.Replace('\\', '/'),
                record,
                cancellationToken);
        }

        var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
        await using var manifestStream = manifestEntry.Open();
        await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken);
    }

    private static async Task<BackupFileEntry> WriteRecordEntryAsync(
        ZipArchive archive,
        string entryName,
        SourceRecordFile source,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var input = new FileStream(
            source.FullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = entry.Open();
        using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long totalBytes = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            incrementalHash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            totalBytes += read;
        }

        var hash = Convert.ToHexString(incrementalHash.GetHashAndReset());
        if (!string.IsNullOrWhiteSpace(source.ExpectedSha256) &&
            !string.Equals(hash, source.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(
                $"The record '{source.RelativePath}' changed while the differential backup was being created. " +
                "The backup was discarded and will be retried.");
        }

        return new BackupFileEntry
        {
            RelativePath = source.RelativePath,
            ArchiveEntryName = entryName,
            Sha256 = hash,
            SizeBytes = totalBytes,
            LastWriteTime = new DateTimeOffset(source.LastWriteTimeUtc, TimeSpan.Zero)
        };
    }

    private async Task ExtractCoreRestoreEntriesAsync(
        string backupPath,
        string databasePath,
        string settingsPath,
        bool restoreSettings,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(backupPath);
        var databaseEntry = GetRequiredEntry(archive, DatabaseEntryName);
        await ExtractEntryAsync(databaseEntry, databasePath, cancellationToken);
        var settingsEntry = archive.GetEntry(SettingsEntryName);
        if (restoreSettings && settingsEntry is not null)
        {
            await ExtractEntryAsync(settingsEntry, settingsPath, cancellationToken);
        }
    }

    private static async Task ExtractRecordFilesAsync(
        string backupPath,
        IEnumerable<BackupFileEntry> records,
        string destinationRoot,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(backupPath);
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateRecordEntry(record);
            var entry = GetRequiredEntry(archive, record.ArchiveEntryName);
            var destination = BuildSafeChildPath(destinationRoot, record.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination) && !overwrite)
            {
                throw new InvalidDataException(
                    $"Duplicate backup record path detected: {record.RelativePath}");
            }
            await ExtractEntryAsync(entry, destination, cancellationToken, overwrite);
        }
    }

    private static async Task VerifyStagedRecordFilesAsync(
        string stagedRoot,
        IEnumerable<BackupFileEntry> records,
        CancellationToken cancellationToken)
    {
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateRecordEntry(record);
            var path = BuildSafeChildPath(stagedRoot, record.RelativePath);
            if (!File.Exists(path))
            {
                throw new InvalidDataException(
                    $"The staged backup record is missing: {record.RelativePath}");
            }

            var info = new FileInfo(path);
            if (info.Length != record.SizeBytes)
            {
                throw new InvalidDataException(
                    $"The staged backup record has an unexpected size: {record.RelativePath}");
            }

            var hash = await ComputeSha256Async(path, cancellationToken);
            if (!string.Equals(hash, record.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The staged backup record failed SHA-256 verification: {record.RelativePath}");
            }
        }
    }

    private static async Task ApplyStagedRecordFilesAsync(
        RestoreMarker marker,
        string safetyRecordsRoot,
        CancellationToken cancellationToken)
    {
        foreach (var record in marker.RecordFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stagedPath = BuildSafeChildPath(marker.StagedRecordsDirectory, record.RelativePath);
            var targetPath = BuildSafeChildPath(marker.TargetCompletedPdfRoot, record.RelativePath);
            var safetyPath = BuildSafeChildPath(safetyRecordsRoot, record.RelativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            if (File.Exists(targetPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(safetyPath)!);
                File.Copy(targetPath, safetyPath, overwrite: true);
            }

            var temporaryTarget = Path.Combine(
                Path.GetDirectoryName(targetPath)!,
                $".{Path.GetFileName(targetPath)}-{Guid.NewGuid():N}.restore.tmp");
            try
            {
                File.Copy(stagedPath, temporaryTarget, overwrite: false);
                var copiedHash = await ComputeSha256Async(temporaryTarget, cancellationToken);
                if (!string.Equals(copiedHash, record.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"The restored record failed verification before replacement: {record.RelativePath}");
                }
                File.Move(temporaryTarget, targetPath, overwrite: true);
            }
            finally
            {
                DeleteIfExists(temporaryTarget);
            }
        }
    }

    private async Task<string> FindBaseFullBackupAsync(
        string differentialPath,
        BackupManifest differentialManifest,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(differentialManifest.BaseFullBackupId))
        {
            throw new InvalidDataException("The differential backup does not identify its Monday full backup.");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(differentialPath))!;
        if (!string.IsNullOrWhiteSpace(differentialManifest.BaseFullBackupFileName))
        {
            var safeName = Path.GetFileName(differentialManifest.BaseFullBackupFileName);
            var candidate = Path.Combine(directory, safeName);
            if (File.Exists(candidate))
            {
                var manifest = await GetManifestAsync(candidate, cancellationToken);
                if (string.Equals(
                        manifest.BackupId,
                        differentialManifest.BaseFullBackupId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        foreach (var candidate in Directory.EnumerateFiles(
                     directory,
                     ScheduledBackupPrefix + "Full-*.zip",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var manifest = await GetManifestAsync(candidate, cancellationToken);
                if (string.Equals(
                        manifest.BackupId,
                        differentialManifest.BaseFullBackupId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Continue searching. Full verification occurs after the match.
            }
        }

        throw new FileNotFoundException(
            "The Monday full backup required by this differential was not found in the same backup folder.");
    }

    private async Task CreateConsistentDatabaseCopyAsync(
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var sourceConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _paths.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();
        var destinationConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();

        await using var source = new SqliteConnection(sourceConnectionString);
        await source.OpenAsync(cancellationToken);
        await using var destination = new SqliteConnection(destinationConnectionString);
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
    }

    private static async Task VerifyDatabaseFileAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        var result = Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The restored database failed SQLite integrity checking: {result}");
        }
    }

    private static async Task<HashSet<string>> ValidateProtectedRecordPathsAsync(
        string databasePath,
        string completedPdfRoot,
        CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();

        var protectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var protectedRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT PdfPath
            FROM Transactions
            WHERE LENGTH(TRIM(PdfPath)) > 0
            UNION
            SELECT Path
            FROM FileArtifacts
            WHERE ArtifactType IN (
                'OriginalSignedIntake',
                'FinalSignedCloseout',
                'SignedPartialPickup')
              AND LENGTH(TRIM(Path)) > 0;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            protectedPaths.Add(reader.GetString(0));
        }

        foreach (var protectedPath in protectedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Path.IsPathFullyQualified(protectedPath))
            {
                throw new InvalidDataException(
                    $"An official 1297 record uses a relative path and cannot be protected by backup: {protectedPath}");
            }

            var fullPath = Path.GetFullPath(protectedPath);
            string relativePath;
            try
            {
                relativePath = NormalizeRelativePath(completedPdfRoot, fullPath);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
            {
                throw new InvalidDataException(
                    $"An official 1297 record is outside the configured completed-PDF folder: {protectedPath}",
                    ex);
            }

            if (!string.Equals(
                    Path.GetExtension(fullPath),
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"An official 1297 record does not use a PDF file path: {protectedPath}");
            }
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    "An official 1297 record referenced by the database is missing. " +
                    "The scheduled backup was stopped so it cannot be treated as complete.",
                    fullPath);
            }

            EnsureNoChildReparsePoint(completedPdfRoot, relativePath);
            protectedRelativePaths.Add(relativePath.Replace('\\', '/'));
        }

        return protectedRelativePaths;
    }

    private static void EnsureNoChildReparsePoint(string root, string relativePath)
    {
        var current = Path.GetFullPath(root);
        foreach (var segment in relativePath.Split(
                     new[] { '/', '\\' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    $"An official 1297 record crosses a reparse point that scheduled backup will not follow: {current}");
            }
        }
    }

    private static async Task RemapDatabaseRecordPathsAsync(
        string databasePath,
        string sourceRoot,
        string targetRoot,
        CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        try
        {
            await RemapTablePathsAsync(
                connection,
                transaction,
                "Transactions",
                "Id",
                "PdfPath",
                sourceRoot,
                targetRoot,
                cancellationToken);
            await RemapTablePathsAsync(
                connection,
                transaction,
                "FileArtifacts",
                "Id",
                "Path",
                sourceRoot,
                targetRoot,
                cancellationToken);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        await VerifyDatabaseFileAsync(databasePath, cancellationToken);
    }

    private static async Task RemapTablePathsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string idColumn,
        string pathColumn,
        string sourceRoot,
        string targetRoot,
        CancellationToken cancellationToken)
    {
        var values = new List<(object Id, string Path)>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = $"SELECT {idColumn}, {pathColumn} FROM {tableName};";
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                values.Add((reader.GetValue(0), reader.GetString(1)));
            }
        }

        foreach (var value in values)
        {
            var remapped = TryRemapPath(value.Path, sourceRoot, targetRoot);
            if (remapped is null)
            {
                continue;
            }

            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = $"UPDATE {tableName} SET {pathColumn}=$path WHERE {idColumn}=$id;";
            update.Parameters.AddWithValue("$path", remapped);
            update.Parameters.AddWithValue("$id", value.Id);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task RemapStagedSettingsAsync(
        string settingsPath,
        string targetRoot,
        CancellationToken cancellationToken)
    {
        AppSettings? settings;
        await using (var input = File.OpenRead(settingsPath))
        {
            settings = await JsonSerializer.DeserializeAsync<AppSettings>(
                input,
                JsonOptions,
                cancellationToken);
        }
        if (settings is null)
        {
            throw new InvalidDataException("The settings in the backup could not be read for path remapping.");
        }

        settings.CompletedPdfFolder = targetRoot;
        await using var output = File.Create(settingsPath);
        await JsonSerializer.SerializeAsync(output, settings, JsonOptions, cancellationToken);
        await output.FlushAsync(cancellationToken);
        output.Flush(flushToDisk: true);
    }

    private static async Task<BackupManifest> ReadManifestAsync(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        var manifestEntry = GetRequiredEntry(archive, ManifestEntryName);
        if (manifestEntry.Length <= 0 || manifestEntry.Length > MaximumManifestBytes)
        {
            throw new InvalidDataException("The backup manifest size is invalid.");
        }

        await using var manifestStream = manifestEntry.Open();
        return await JsonSerializer.DeserializeAsync<BackupManifest>(
                manifestStream,
                JsonOptions,
                cancellationToken)
            ?? throw new InvalidDataException("The backup manifest could not be read.");
    }

    private static void ValidateManifest(BackupManifest manifest)
    {
        var isLegacy = string.Equals(
            manifest.BackupType,
            BackupManifest.LegacyBackupType,
            StringComparison.OrdinalIgnoreCase);
        var isFull = string.Equals(
            manifest.BackupType,
            BackupManifest.FullBackupType,
            StringComparison.OrdinalIgnoreCase);
        var isDifferential = string.Equals(
            manifest.BackupType,
            BackupManifest.DifferentialBackupType,
            StringComparison.OrdinalIgnoreCase);
        if (!isLegacy && !isFull && !isDifferential)
        {
            throw new InvalidDataException("The backup manifest type is not supported.");
        }
        if (manifest.FormatVersion < 1 || manifest.FormatVersion > BackupManifest.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Backup format version {manifest.FormatVersion} is not supported.");
        }
        if (manifest.DatabaseSchemaVersion < 0)
        {
            throw new InvalidDataException("The backup manifest database schema version is invalid.");
        }
        if (!IsSha256(manifest.DatabaseSha256))
        {
            throw new InvalidDataException("The backup manifest database hash is invalid.");
        }
        if (manifest.FormatVersion >= 2 && manifest.IncludesSettings &&
            !IsSha256(manifest.SettingsSha256))
        {
            throw new InvalidDataException("The backup manifest settings hash is invalid.");
        }
        if (manifest.RecordFiles is null)
        {
            throw new InvalidDataException("The backup manifest record list is invalid.");
        }
        if (manifest.DeletedRecordPaths is null)
        {
            throw new InvalidDataException("The backup manifest deleted-record list is invalid.");
        }
        if (manifest.RecordFiles.Count > MaximumRecordFileCount)
        {
            throw new InvalidDataException("The backup contains more record files than the application supports.");
        }
        if (manifest.DeletedRecordPaths.Count > MaximumRecordFileCount)
        {
            throw new InvalidDataException("The backup contains more deleted-record paths than the application supports.");
        }

        var relativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in manifest.RecordFiles)
        {
            ValidateRecordEntry(record);
            if (!relativePaths.Add(record.RelativePath) || !entryNames.Add(record.ArchiveEntryName))
            {
                throw new InvalidDataException("The backup manifest contains duplicate record paths.");
            }
        }

        var deletedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var deletedPath in manifest.DeletedRecordPaths)
        {
            ValidateRelativeRecordPath(deletedPath);
            if (!deletedPaths.Add(deletedPath) || relativePaths.Contains(deletedPath))
            {
                throw new InvalidDataException(
                    "The backup manifest contains duplicate or conflicting deleted-record paths.");
            }
        }

        if (isFull || isDifferential)
        {
            if (manifest.BackupId is not { Length: 32 } ||
                !manifest.BackupId.All(Uri.IsHexDigit) ||
                string.IsNullOrWhiteSpace(manifest.CompletedPdfRoot) ||
                !Path.IsPathFullyQualified(manifest.CompletedPdfRoot))
            {
                throw new InvalidDataException("The scheduled backup manifest is missing its ID or records root.");
            }
        }
        if (isDifferential &&
            (manifest.BaseFullBackupId is not { Length: 32 } ||
             !manifest.BaseFullBackupId.All(Uri.IsHexDigit) ||
             string.IsNullOrWhiteSpace(manifest.BaseFullBackupFileName) ||
             manifest.BaseFullBackupFileName.Length > 260 ||
             !string.Equals(
                 manifest.BaseFullBackupFileName,
                 Path.GetFileName(manifest.BaseFullBackupFileName),
                 StringComparison.Ordinal) ||
             !string.Equals(
                 Path.GetExtension(manifest.BaseFullBackupFileName),
                 ".zip",
                 StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The differential backup does not identify a full-backup base.");
        }
        if (!isDifferential &&
            (!string.IsNullOrWhiteSpace(manifest.BaseFullBackupId) ||
             !string.IsNullOrWhiteSpace(manifest.BaseFullBackupFileName)))
        {
            throw new InvalidDataException("Only a differential backup may identify a full-backup base.");
        }
        if (!isDifferential && manifest.DeletedRecordPaths.Count > 0)
        {
            throw new InvalidDataException(
                "Only a differential backup may contain deleted-record paths.");
        }
        if (isLegacy && manifest.RecordFiles.Count > 0)
        {
            throw new InvalidDataException("A legacy database-only backup cannot contain record files.");
        }
    }

    private static void ValidateRecordEntry(BackupFileEntry? record)
    {
        if (record is null)
        {
            throw new InvalidDataException("The backup manifest contains an empty record entry.");
        }
        ValidateRelativeRecordPath(record.RelativePath);
        var expectedArchiveEntry = RecordsEntryPrefix + record.RelativePath.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(record.ArchiveEntryName) ||
            !string.Equals(
                record.ArchiveEntryName,
                expectedArchiveEntry,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("A backup record contains an unsafe archive entry name.");
        }
        if (record.SizeBytes < 0 || !IsSha256(record.Sha256))
        {
            throw new InvalidDataException("A backup record contains invalid size or hash metadata.");
        }
    }

    private static void ValidateRelativeRecordPath(string relativePath)
    {
        string[] parts = string.IsNullOrWhiteSpace(relativePath)
            ? []
            : relativePath.Split(new[] { '/', '\\' });
        if (string.IsNullOrWhiteSpace(relativePath) ||
            relativePath.Length > 1024 ||
            Path.IsPathRooted(relativePath) ||
            parts.Length == 0 ||
            parts.Any(part =>
                part is ".." or "." or "" ||
                part.Length > 255 ||
                part.EndsWith(' ') ||
                part.EndsWith('.') ||
                part.Any(character =>
                    character < 32 ||
                    character is '<' or '>' or ':' or '"' or '|' or '?' or '*') ||
                IsReservedWindowsFileName(part)) ||
            !string.Equals(Path.GetExtension(relativePath), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A backup record contains an unsafe relative PDF path.");
        }
    }

    private static bool IsReservedWindowsFileName(string segment)
    {
        var dotIndex = segment.IndexOf('.');
        var stem = dotIndex < 0 ? segment : segment[..dotIndex];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return stem.Length == 4 &&
               (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
               stem[3] is >= '1' and <= '9';
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static void EnsureNoDuplicateEntries(ZipArchive archive)
    {
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (!entries.Add(entry.FullName))
            {
                throw new InvalidDataException($"The backup contains a duplicate ZIP entry: {entry.FullName}");
            }
        }
    }

    private static ZipArchiveEntry GetRequiredEntry(ZipArchive archive, string entryName) =>
        archive.GetEntry(entryName)
        ?? throw new InvalidDataException($"The backup entry '{entryName}' is missing.");

    private static async Task ExtractEntryAsync(
        ZipArchiveEntry entry,
        string destinationPath,
        CancellationToken cancellationToken,
        bool overwrite = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var mode = overwrite ? FileMode.Create : FileMode.CreateNew;
        await using var input = entry.Open();
        await using var output = new FileStream(
            destinationPath,
            mode,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, 128 * 1024, cancellationToken);
        await output.FlushAsync(cancellationToken);
        output.Flush(flushToDisk: true);
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static async Task<string> ComputeSha256Async(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static string NormalizeRelativePath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The record path is outside the completed-PDF folder: {path}");
        }
        return relative.Replace('\\', '/');
    }

    private static string BuildSafeChildPath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, normalized));
        var relative = Path.GetRelativePath(fullRoot, candidate);
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException("A backup record path escapes its approved destination.");
        }
        return candidate;
    }

    private static string? TryRemapPath(string path, string sourceRoot, string targetRoot)
    {
        try
        {
            var relative = Path.GetRelativePath(Path.GetFullPath(sourceRoot), Path.GetFullPath(path));
            if (Path.IsPathRooted(relative) ||
                relative.Equals("..", StringComparison.Ordinal) ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return null;
            }
            return Path.GetFullPath(Path.Combine(targetRoot, relative));
        }
        catch
        {
            return null;
        }
    }

    private static bool PathsEqual(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsOverlap(string first, string second)
    {
        var firstFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(first));
        var secondFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(second));
        return IsSameOrChild(firstFull, secondFull) || IsSameOrChild(secondFull, firstFull);
    }

    private static bool IsSameOrChild(string path, string possibleParent)
    {
        if (string.Equals(path, possibleParent, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return path.StartsWith(
            possibleParent + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeFilePart(string value)
    {
        var safe = new string((value ?? string.Empty)
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Take(40)
            .ToArray());
        return safe.Length == 0 ? "manual" : safe;
    }

    private static string GetApplicationVersion() =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "unknown";

    private static void WriteHashSidecar(string backupPath, string archiveHash)
    {
        var finalPath = backupPath + ".sha256";
        var temporaryPath = finalPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            $"{archiveHash.ToLowerInvariant()}  {Path.GetFileName(backupPath)}{Environment.NewLine}",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, finalPath, overwrite: true);
    }

    private void TryDeleteBackupAndSidecar(string path)
    {
        try
        {
            File.Delete(path);
            DeleteIfExists(path + ".sha256");
            _logger.Information($"Removed expired scheduled backup: {path}");
        }
        catch (Exception ex)
        {
            _logger.Warning($"Backup retention could not remove '{path}': {ex.Message}");
        }
    }

    private static void CopyIfExists(string source, string destination)
    {
        if (File.Exists(source))
        {
            File.Copy(source, destination, overwrite: true);
        }
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary cleanup is best effort.
        }
    }

    private static string BuildUniquePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; index <= 999; index++)
        {
            candidate = Path.Combine(directory, $"{baseName}-{index}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{baseName}-{Guid.NewGuid():N}{extension}");
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Temporary cleanup is best effort.
        }
    }

    private sealed record SourceRecordFile(
        string FullPath,
        string RelativePath,
        DateTime LastWriteTimeUtc,
        string ExpectedSha256 = "");

    private sealed class RestoreMarker
    {
        public string SourceBackupPath { get; init; } = string.Empty;
        public string StagedDatabasePath { get; init; } = string.Empty;
        public string StagedSettingsPath { get; init; } = string.Empty;
        public bool RestoreSettings { get; init; }
        public string SettingsSha256 { get; init; } = string.Empty;
        public string DatabaseSha256 { get; init; } = string.Empty;
        public int DatabaseSchemaVersion { get; init; }
        public DateTimeOffset StagedAt { get; init; }
        public string StagedRecordsDirectory { get; init; } = string.Empty;
        public string SourceCompletedPdfRoot { get; init; } = string.Empty;
        public string TargetCompletedPdfRoot { get; init; } = string.Empty;
        public List<BackupFileEntry> RecordFiles { get; init; } = [];
    }
}
