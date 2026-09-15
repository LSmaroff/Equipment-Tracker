using System.IO;
using System.Text;
using System.Text.Json;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;

namespace EquipmentTracking.App.Services;

public sealed class WorkflowJournalService : IDisposable
{
    public const string IntakeOperation = "IntakeFinalization";
    public const string CloseoutOperation = "Closeout";
    public const string PartialPickupOperation = "PartialPickup";
    public const string PendingStatus = "Pending";
    public const string FailedStatus = "Failed";
    public const string CompletedStatus = "Completed";
    public const string AbandonedStatus = "Abandoned";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly AppPaths _paths;
    private readonly FileLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public WorkflowJournalService(AppPaths paths, FileLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public WorkflowJournalEntry CreateIntake(
        WorkingTransaction working,
        CustomerIdentity customer,
        string phoneNumber,
        string technician,
        string organization,
        string ticketNumber,
        IReadOnlyList<DeviceRecord> devices)
    {
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(devices);

        return new WorkflowJournalEntry
        {
            OperationId = $"intake-{working.TransactionId}",
            OperationType = IntakeOperation,
            TransactionId = working.TransactionId,
            Stage = "PreparedPdf",
            Status = PendingStatus,
            StartedAt = working.CreatedAt,
            UpdatedAt = DateTimeOffset.Now,
            SourcePdfPath = working.PreparedPdfPath,
            Intake = new PendingIntakePayload
            {
                Working = working,
                Customer = CloneCustomer(customer),
                PhoneNumber = phoneNumber,
                Technician = technician,
                Organization = organization,
                TicketNumber = ticketNumber,
                Devices = devices.Select(CloneDevice).ToList()
            }
        };
    }

    public WorkflowJournalEntry CreateCloseout(
        EquipmentTransaction transaction,
        DateTimeOffset signatureSearchStartedAt,
        IReadOnlyCollection<string> existingSignatureFingerprints)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(existingSignatureFingerprints);

        return new WorkflowJournalEntry
        {
            OperationId = $"closeout-{transaction.Id}",
            OperationType = CloseoutOperation,
            TransactionId = transaction.Id,
            Stage = "PdfOpenedForPickupSignature",
            Status = PendingStatus,
            StartedAt = signatureSearchStartedAt,
            UpdatedAt = DateTimeOffset.Now,
            SourcePdfPath = transaction.PdfPath,
            Closeout = new PendingCloseoutPayload
            {
                TransactionId = transaction.Id,
                SignatureSearchStartedAt = signatureSearchStartedAt,
                ExistingSignatureFingerprints = existingSignatureFingerprints.ToList()
            }
        };
    }

    public WorkflowJournalEntry CreatePartialPickup(
        EquipmentTransaction transaction,
        string pickupId,
        string preparedPdfPath,
        IReadOnlyCollection<long> deviceIds,
        int sequenceNumber,
        string technician,
        string notes,
        DateTimeOffset pickedUpAt,
        IReadOnlyCollection<string> existingSignatureFingerprints)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(pickupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(preparedPdfPath);
        ArgumentNullException.ThrowIfNull(deviceIds);
        ArgumentNullException.ThrowIfNull(existingSignatureFingerprints);
        var createdAt = DateTimeOffset.Now;

        return new WorkflowJournalEntry
        {
            OperationId = pickupId,
            OperationType = PartialPickupOperation,
            TransactionId = transaction.Id,
            Stage = "PdfPreparedForPickupSignature",
            Status = PendingStatus,
            StartedAt = pickedUpAt,
            UpdatedAt = createdAt,
            SourcePdfPath = preparedPdfPath,
            PartialPickup = new PendingPartialPickupPayload
            {
                PickupId = pickupId,
                TransactionId = transaction.Id,
                DeviceIds = deviceIds.Distinct().ToList(),
                SequenceNumber = sequenceNumber,
                Technician = technician.Trim(),
                Notes = (notes ?? string.Empty).Trim(),
                PickedUpAt = pickedUpAt,
                CreatedAt = createdAt,
                SignatureSearchStartedAt = createdAt,
                ExistingSignatureFingerprints =
                    existingSignatureFingerprints.ToList()
            }
        };
    }

    public void Save(WorkflowJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _gate.Wait();
        try
        {
            _paths.EnsureDirectories();
            entry.UpdatedAt = DateTimeOffset.Now;
            var path = GetPath(entry.OperationId);
            var temporaryPath = path + ".tmp";
            var json = JsonSerializer.Serialize(entry, JsonOptions);
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            CommitTemporaryFile(temporaryPath, path);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        WorkflowJournalEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _paths.EnsureDirectories();
            entry.UpdatedAt = DateTimeOffset.Now;
            var path = GetPath(entry.OperationId);
            var temporaryPath = path + ".tmp";
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, entry, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            CommitTemporaryFile(temporaryPath, path);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WorkflowJournalEntry?> GetAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(operationId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<WorkflowJournalEntry>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Error($"Workflow journal '{operationId}' could not be read.", ex);
            return null;
        }
    }

    public async Task<IReadOnlyList<WorkflowJournalEntry>> GetPendingAsync(
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureDirectories();
        var entries = new List<WorkflowJournalEntry>();

        foreach (var path in Directory.EnumerateFiles(_paths.RecoveryDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(path);
                var entry = await JsonSerializer.DeserializeAsync<WorkflowJournalEntry>(
                    stream,
                    JsonOptions,
                    cancellationToken);
                if (entry is not null &&
                    !string.Equals(entry.Status, CompletedStatus, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(entry.Status, AbandonedStatus, StringComparison.OrdinalIgnoreCase))
                {
                    entries.Add(entry);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"A workflow recovery file could not be read: {Path.GetFileName(path)} ({ex.GetType().Name}).");
            }
        }

        return entries
            .OrderByDescending(item => item.UpdatedAt)
            .ToArray();
    }

    public async Task UpdateStageAsync(
        WorkflowJournalEntry entry,
        string stage,
        string? destinationPath = null,
        string? sourceHash = null,
        string? destinationHash = null,
        CancellationToken cancellationToken = default)
    {
        entry.Stage = stage;
        entry.Status = PendingStatus;
        entry.LastError = string.Empty;
        if (!string.IsNullOrWhiteSpace(destinationPath))
        {
            entry.DestinationPdfPath = destinationPath;
        }
        if (!string.IsNullOrWhiteSpace(sourceHash))
        {
            entry.SourceSha256 = sourceHash;
        }
        if (!string.IsNullOrWhiteSpace(destinationHash))
        {
            entry.DestinationSha256 = destinationHash;
        }
        await SaveAsync(entry, cancellationToken);
    }

    public async Task MarkFailedAsync(
        WorkflowJournalEntry entry,
        string stage,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        entry.Stage = stage;
        entry.Status = FailedStatus;
        entry.RetryCount++;
        entry.LastError = exception.ToString();
        await SaveAsync(entry, cancellationToken);
    }

    public Task MarkCompletedAsync(
        WorkflowJournalEntry entry,
        CancellationToken cancellationToken = default)
    {
        entry.Stage = "Completed";
        entry.Status = CompletedStatus;
        entry.LastError = string.Empty;
        return SaveAsync(entry, cancellationToken);
    }

    public Task MarkAbandonedAsync(
        WorkflowJournalEntry entry,
        string reason,
        CancellationToken cancellationToken = default)
    {
        entry.Stage = "Abandoned";
        entry.Status = AbandonedStatus;
        entry.LastError = reason;
        return SaveAsync(entry, cancellationToken);
    }

    public void DeleteAll()
    {
        _paths.EnsureDirectories();
        foreach (var path in Directory.EnumerateFiles(_paths.RecoveryDirectory, "*.json"))
        {
            File.Delete(path);
        }
    }

    public void CleanupCompleted(int retentionDays)
    {
        _paths.EnsureDirectories();
        var cutoff = DateTimeOffset.Now.AddDays(-Math.Clamp(retentionDays, 1, 3650));
        foreach (var path in Directory.EnumerateFiles(_paths.RecoveryDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(path);
                var entry = JsonSerializer.Deserialize<WorkflowJournalEntry>(json, JsonOptions);
                if (entry is not null &&
                    (string.Equals(entry.Status, CompletedStatus, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(entry.Status, AbandonedStatus, StringComparison.OrdinalIgnoreCase)) &&
                    entry.UpdatedAt < cutoff)
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Maintenance is best effort and must not block startup.
            }
        }
    }


    private static void CommitTemporaryFile(string temporaryPath, string finalPath)
    {
        IOException? lastError = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                File.Move(temporaryPath, finalPath, overwrite: true);
                return;
            }
            catch (IOException ex)
            {
                lastError = ex;
                if (attempt < 5)
                {
                    Thread.Sleep(50 * attempt);
                }
            }
        }

        throw new IOException(
            $"The workflow recovery journal could not be committed to '{finalPath}'.",
            lastError);
    }

    private string GetPath(string operationId)
    {
        var safeName = new string(operationId
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Take(180)
            .ToArray());
        if (safeName.Length == 0)
        {
            throw new InvalidOperationException("The workflow operation identifier is invalid.");
        }

        return Path.Combine(_paths.RecoveryDirectory, safeName + ".json");
    }

    private static CustomerIdentity CloneCustomer(CustomerIdentity source) => new()
    {
        Rank = source.Rank,
        FirstName = source.FirstName,
        MiddleInitial = source.MiddleInitial,
        LastName = source.LastName,
        OriginalName = source.OriginalName
    };

    private static DeviceRecord CloneDevice(DeviceRecord source) => new()
    {
        Id = source.Id,
        PartNumber = string.IsNullOrWhiteSpace(source.PartNumber)
            ? source.Model
            : source.PartNumber,
        Model = source.Model,
        SerialNumber = source.SerialNumber,
        AssetTag = source.AssetTag,
        RawScanValue = source.RawScanValue,
        Status = source.Status,
        ReturnedAt = source.ReturnedAt,
        ReturnCondition = source.ReturnCondition,
        ReturnNotes = source.ReturnNotes
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

}
