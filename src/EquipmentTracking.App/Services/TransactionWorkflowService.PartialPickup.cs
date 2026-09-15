using System.IO;
using EquipmentTracking.App.Models;

namespace EquipmentTracking.App.Services;

public sealed partial class TransactionWorkflowService
{
    public async Task<PartialPickupPreparationResult> BeginPartialPickupAsync(
        string transactionId,
        IReadOnlyList<long> deviceIds,
        string technician,
        string notes,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentNullException.ThrowIfNull(deviceIds);
        ValidatePartialPickupOperatorInput(technician, notes);

        if (deviceIds.Count == 0)
        {
            throw new InvalidOperationException(
                "Select at least one outstanding device for partial pickup.");
        }

        var selectedIds = deviceIds.ToArray();
        if (selectedIds.Any(id => id <= 0) ||
            selectedIds.Distinct().Count() != selectedIds.Length)
        {
            throw new InvalidOperationException(
                "The partial-pickup device selection must contain distinct valid device identifiers.");
        }

        var transaction = await _database.GetTransactionByIdAsync(
            transactionId.Trim(),
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The selected 1297 transaction could not be found.");

        if (transaction.IsArchived || transaction.ClosedAt is not null)
        {
            throw new InvalidOperationException(
                "This 1297 is already archived and cannot start a partial pickup.");
        }
        if (transaction.CloseoutPreparedAt is not null)
        {
            throw new InvalidOperationException(
                "This 1297 already has a closeout in progress. Resume or roll back that workflow first.");
        }

        await EnsureNoPendingCloseoutOrPartialPickupAsync(
            transaction.Id,
            cancellationToken);

        // The position on the 1297 is defined by the complete DB Id-ordered device
        // list. Returned rows remain in this list so a later row is never shifted
        // onto the wrong DeviceN PDF field.
        var selectedSet = selectedIds.ToHashSet();
        var selectedDevices = transaction.Devices
            .Select((device, index) => new
            {
                Device = device,
                LogicalFieldName = $"Device{index + 1}"
            })
            .Where(item => selectedSet.Contains(item.Device.Id))
            .ToArray();

        if (selectedDevices.Length != selectedIds.Length)
        {
            throw new InvalidOperationException(
                "Every selected device must belong to the selected 1297.");
        }
        if (selectedDevices.Any(item =>
                string.Equals(
                    item.Device.Status,
                    DeviceStatusCatalog.Returned,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "A device that is already returned cannot be included in another pickup receipt.");
        }

        var originalArtifact = (await _database.GetFileArtifactsAsync(
                transaction.Id,
                cancellationToken))
            .FirstOrDefault(artifact =>
                string.Equals(
                    artifact.ArtifactType,
                    "OriginalSignedIntake",
                    StringComparison.OrdinalIgnoreCase) &&
                File.Exists(artifact.Path))
            ?? throw new InvalidOperationException(
                "The preserved original signed intake PDF is unavailable. Partial pickup cannot modify or substitute the working parent PDF.");

        await WaitForStableExclusiveReadAsync(
            originalArtifact.Path,
            cancellationToken);
        var originalHashBefore = await ComputeFileSha256Async(
            originalArtifact.Path,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(originalArtifact.Sha256) &&
            !string.Equals(
                originalHashBefore,
                originalArtifact.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The preserved original signed intake PDF does not match its recorded SHA-256.");
        }
        if (originalArtifact.SizeBytes > 0 &&
            new FileInfo(originalArtifact.Path).Length != originalArtifact.SizeBytes)
        {
            throw new InvalidDataException(
                "The preserved original signed intake PDF does not match its recorded size.");
        }

        var completedRoot = _settings.ResolvePath(
            _settings.Current.CompletedPdfFolder);
        if (string.IsNullOrWhiteSpace(completedRoot))
        {
            throw new InvalidOperationException(
                "The completed-PDF folder is not configured.");
        }

        var sequenceNumber = await _database.GetNextPickupSequenceAsync(
            transaction.Id,
            cancellationToken);
        var pickupId =
            $"partial-pickup-{transaction.Id}-{Guid.NewGuid():N}";
        var receiptFolder = Path.Combine(
            completedRoot,
            "Pickup receipts",
            _fileNames.NormalizeDirectoryName(transaction.Id));
        Directory.CreateDirectory(receiptFolder);
        var baseName =
            $"{transaction.Id}-pickup-{sequenceNumber:D2}-{startedAt:yyyyMMdd-HHmmss}-{pickupId[^8..]}";
        var preparedPath = _fileNames.EnsureUniquePath(
            receiptFolder,
            baseName + "-prepared.pdf");

        WorkflowJournalEntry? journal = null;
        var journalSaved = false;
        try
        {
            _pdf.CreatePartialPickupPdf(
                originalArtifact.Path,
                preparedPath,
                selectedDevices.Select(item => item.LogicalFieldName).ToArray(),
                startedAt,
                _settings.Current);

            var originalHashAfter = await ComputeFileSha256Async(
                originalArtifact.Path,
                cancellationToken);
            if (!string.Equals(
                    originalHashBefore,
                    originalHashAfter,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "The preserved original signed intake PDF changed while its child pickup receipt was prepared.");
            }

            await WaitForStableExclusiveReadAsync(
                preparedPath,
                cancellationToken);
            var existingPickupSignature = await _signature.ExtractAsync(
                preparedPath,
                AppSettings.PickupSignatureFieldName,
                preferredFieldOnly: true,
                cancellationToken: cancellationToken);
            if (existingPickupSignature.SignatureFound)
            {
                var diagnostics = await _signature.DiagnoseAsync(
                    preparedPath,
                    AppSettings.PickupSignatureFieldName,
                    existingSignatureFingerprints: null,
                    searchStartedAt: startedAt,
                    cancellationToken: cancellationToken);
                throw new SignatureValidationException(
                    "The preserved intake already contains a signature in the Pickup Signature field. A new partial-pickup receipt cannot safely reuse it.",
                    diagnostics);
            }

            var signatureBaseline =
                await _signature.GetSignatureFingerprintsAsync(
                    preparedPath,
                    cancellationToken);
            journal = _journals.CreatePartialPickup(
                transaction,
                pickupId,
                preparedPath,
                selectedIds,
                sequenceNumber,
                technician,
                notes,
                startedAt,
                signatureBaseline);
            journal.OriginalSignedPdfPath = originalArtifact.Path;
            journal.SourceSha256 = await ComputeFileSha256Async(
                preparedPath,
                cancellationToken);
            await _journals.SaveAsync(journal, cancellationToken);
            journalSaved = true;

            _logger.Workflow(
                transaction.Id,
                WorkflowJournalService.PartialPickupOperation,
                journal.Stage,
                $"Prepared pickup receipt {pickupId} for {selectedIds.Length} device(s); Adobe may now be opened.");

            return new PartialPickupPreparationResult
            {
                OperationId = pickupId,
                PreparedPdfPath = preparedPath,
                Transaction = transaction,
                SequenceNumber = sequenceNumber,
                DeviceIds = selectedIds,
                Journal = journal
            };
        }
        catch
        {
            // Before the durable journal exists, this is only an unsigned generated
            // child. Once journaled, it is recovery evidence and is never removed
            // here, including when the caller cancels its UI.
            if (!journalSaved && File.Exists(preparedPath))
            {
                TryDelete(preparedPath);
            }
            throw;
        }
    }

    public async Task<PartialPickupResult> FinalizePartialPickupAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        var journal = await _journals.GetAsync(
            operationId.Trim(),
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The partial-pickup recovery record could not be found.");
        if (!string.Equals(
                journal.OperationType,
                WorkflowJournalService.PartialPickupOperation,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The selected recovery record is not a partial pickup.");
        }
        if (string.Equals(
                journal.Status,
                WorkflowJournalService.AbandonedStatus,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "This partial-pickup attempt was abandoned and cannot be finalized.");
        }

        var payload = journal.PartialPickup
            ?? throw new InvalidOperationException(
                "The partial-pickup recovery record does not contain its saved request.");
        ValidatePartialPickupJournal(journal, payload);

        try
        {
            var committedReceipt = await _database.GetPickupReceiptByIdAsync(
                payload.PickupId,
                cancellationToken);
            if (committedReceipt is not null)
            {
                return await CompleteCommittedPartialPickupRecoveryAsync(
                    journal,
                    payload,
                    committedReceipt,
                    cancellationToken);
            }

            if (!File.Exists(journal.SourcePdfPath))
            {
                throw new FileNotFoundException(
                    "The prepared partial-pickup receipt could not be found.",
                    journal.SourcePdfPath);
            }
            await WaitForStableExclusiveReadAsync(
                journal.SourcePdfPath,
                cancellationToken);
            var validatedSourceHash = await ComputeFileSha256Async(
                journal.SourcePdfPath,
                cancellationToken);

            var pickupSignature = await _signature.ExtractNewSignatureAsync(
                journal.SourcePdfPath,
                payload.ExistingSignatureFingerprints,
                AppSettings.PickupSignatureFieldName,
                preferredFieldOnly: true,
                cancellationToken: cancellationToken);
            if (!pickupSignature.SignatureFound)
            {
                pickupSignature = await TryFindCloseoutSignatureFallbackAsync(
                    journal.SourcePdfPath,
                    AppSettings.PickupSignatureFieldName,
                    payload.ExistingSignatureFingerprints,
                    payload.SignatureSearchStartedAt,
                    pickupSignature.DiagnosticMessage,
                    cancellationToken);
            }

            var hashAfterSignatureRead = await ComputeFileSha256Async(
                journal.SourcePdfPath,
                cancellationToken);
            if (!string.Equals(
                    validatedSourceHash,
                    hashAfterSignatureRead,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "The prepared pickup receipt changed while its new signature was being verified. Save and close Adobe, then resume the workflow.");
            }

            var verifiedCopy = await EnsurePartialPickupSignedSnapshotAsync(
                journal,
                validatedSourceHash,
                cancellationToken);

            await _journals.UpdateStageAsync(
                journal,
                "NewPickupSignatureVerified",
                destinationPath: journal.DestinationPdfPath,
                sourceHash: verifiedCopy.SourceSha256,
                destinationHash: verifiedCopy.DestinationSha256,
                cancellationToken: cancellationToken);

            var receipt = new PickupReceipt
            {
                Id = payload.PickupId,
                TransactionId = payload.TransactionId,
                SequenceNumber = payload.SequenceNumber,
                PdfPath = journal.DestinationPdfPath,
                Technician = payload.Technician,
                Notes = payload.Notes,
                PickedUpAt = payload.PickedUpAt,
                Sha256 = verifiedCopy.DestinationSha256,
                SizeBytes = verifiedCopy.SizeBytes,
                SignerName = pickupSignature.SignerName,
                CertificateSubject = pickupSignature.CertificateSubject,
                CertificateThumbprint = pickupSignature.CertificateThumbprint,
                SignatureTime = pickupSignature.SigningTime,
                CreatedAt = payload.CreatedAt,
                DeviceIds = payload.DeviceIds.ToArray()
            };
            var commit = await _database.CommitPickupReceiptAsync(
                receipt,
                cancellationToken);
            await _journals.UpdateStageAsync(
                journal,
                "PickupReceiptCommitted",
                destinationPath: receipt.PdfPath,
                sourceHash: verifiedCopy.SourceSha256,
                destinationHash: verifiedCopy.DestinationSha256,
                cancellationToken: cancellationToken);

            var transaction = await _database.GetTransactionByIdAsync(
                payload.TransactionId,
                cancellationToken)
                ?? throw new InvalidOperationException(
                    "The partial-pickup parent transaction could not be reloaded after commit.");
            var excelPath = await TryExportAsync(
                transaction.Id,
                cancellationToken);
            await _journals.UpdateStageAsync(
                journal,
                "ExcelRefreshed",
                cancellationToken: cancellationToken);
            await _journals.MarkCompletedAsync(journal, cancellationToken);

            _logger.Workflow(
                transaction.Id,
                WorkflowJournalService.PartialPickupOperation,
                "Completed",
                $"Pickup receipt {receipt.Id} committed for {commit.PickedUpCount} device(s); {commit.RemainingDeviceCount} remain outstanding.");

            return new PartialPickupResult
            {
                PickupDocument = commit.Receipt,
                Transaction = transaction,
                PickupSignature = pickupSignature,
                ExcelExportPath = excelPath,
                UpdatedCount = commit.PickedUpCount,
                RemainingCount = commit.RemainingDeviceCount,
                Archived = commit.ParentArchived,
                WasAlreadyCommitted = commit.WasAlreadyCommitted
            };
        }
        catch (Exception ex)
        {
            await TryMarkJournalFailedAsync(
                journal,
                journal.Stage,
                ex,
                cancellationToken);
            _logger.Workflow(
                payload.TransactionId,
                WorkflowJournalService.PartialPickupOperation,
                journal.Stage,
                "Partial pickup failed and can be resumed from Recovery. " +
                BuildWorkflowFailureContext(journal, ticketNumber: null),
                ex);
            throw;
        }
    }

    private async Task<PartialPickupResult> CompleteCommittedPartialPickupRecoveryAsync(
        WorkflowJournalEntry journal,
        PendingPartialPickupPayload payload,
        PickupReceipt receipt,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(receipt.Id, payload.PickupId, StringComparison.Ordinal) ||
            !string.Equals(
                receipt.TransactionId,
                payload.TransactionId,
                StringComparison.OrdinalIgnoreCase) ||
            receipt.SequenceNumber != payload.SequenceNumber ||
            !string.Equals(
                receipt.Technician,
                payload.Technician,
                StringComparison.Ordinal) ||
            !string.Equals(receipt.Notes, payload.Notes, StringComparison.Ordinal) ||
            receipt.PickedUpAt != payload.PickedUpAt ||
            !receipt.DeviceIds.Order().SequenceEqual(payload.DeviceIds.Order()))
        {
            throw new InvalidDataException(
                "The committed pickup receipt does not match its recovery journal.");
        }

        if (!File.Exists(receipt.PdfPath))
        {
            throw new FileNotFoundException(
                "The committed signed pickup receipt could not be found.",
                receipt.PdfPath);
        }
        await WaitForStableExclusiveReadAsync(receipt.PdfPath, cancellationToken);
        var receiptHash = await ComputeFileSha256Async(
            receipt.PdfPath,
            cancellationToken);
        var receiptSize = new FileInfo(receipt.PdfPath).Length;
        if (string.IsNullOrWhiteSpace(receipt.Sha256) ||
            !string.Equals(
                receiptHash,
                receipt.Sha256,
                StringComparison.OrdinalIgnoreCase) ||
            receiptSize != receipt.SizeBytes)
        {
            throw new InvalidDataException(
                "The committed signed pickup receipt no longer matches its database SHA-256 and size.");
        }
        if ((!string.IsNullOrWhiteSpace(journal.DestinationPdfPath) &&
             !string.Equals(
                 Path.GetFullPath(journal.DestinationPdfPath),
                 Path.GetFullPath(receipt.PdfPath),
                 StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(journal.DestinationSha256) &&
             !string.Equals(
                 journal.DestinationSha256,
                 receiptHash,
                 StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "The committed signed pickup receipt conflicts with its recovery-journal destination.");
        }

        await _journals.UpdateStageAsync(
            journal,
            "PickupReceiptCommitted",
            destinationPath: receipt.PdfPath,
            destinationHash: receiptHash,
            cancellationToken: cancellationToken);
        var transaction = await _database.GetTransactionByIdAsync(
            payload.TransactionId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "The partial-pickup parent transaction could not be reloaded after commit.");
        var remainingCount = transaction.Devices.Count(device =>
            !string.Equals(
                device.Status,
                DeviceStatusCatalog.Returned,
                StringComparison.OrdinalIgnoreCase));
        var excelPath = await TryExportAsync(transaction.Id, cancellationToken);
        await _journals.UpdateStageAsync(
            journal,
            "ExcelRefreshed",
            cancellationToken: cancellationToken);
        await _journals.MarkCompletedAsync(journal, cancellationToken);

        _logger.Workflow(
            transaction.Id,
            WorkflowJournalService.PartialPickupOperation,
            "Completed",
            $"Recovered already-committed pickup receipt {receipt.Id}; {remainingCount} device(s) remain outstanding.");

        return new PartialPickupResult
        {
            PickupDocument = receipt,
            Transaction = transaction,
            PickupSignature = new SignatureInfo
            {
                SignatureFound = true,
                SignerName = receipt.SignerName,
                CertificateSubject = receipt.CertificateSubject,
                CertificateThumbprint = receipt.CertificateThumbprint,
                SigningTime = receipt.SignatureTime,
                DiagnosticMessage =
                    "The already-committed pickup signature metadata was recovered from the protected receipt record."
            },
            ExcelExportPath = excelPath,
            UpdatedCount = receipt.DeviceIds.Count,
            RemainingCount = remainingCount,
            Archived = transaction.IsArchived,
            WasAlreadyCommitted = true
        };
    }

    private async Task<VerifiedCopyResult> EnsurePartialPickupSignedSnapshotAsync(
        WorkflowJournalEntry journal,
        string validatedSourceHash,
        CancellationToken cancellationToken)
    {
        var receiptFolder = Path.GetDirectoryName(journal.SourcePdfPath)
            ?? throw new InvalidOperationException(
                "The partial-pickup receipt folder is invalid.");
        var destinationPath = journal.DestinationPdfPath;
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            var sourceName = Path.GetFileNameWithoutExtension(
                journal.SourcePdfPath);
            var signedName = sourceName.EndsWith(
                    "-prepared",
                    StringComparison.OrdinalIgnoreCase)
                ? sourceName[..^"-prepared".Length] + "-signed.pdf"
                : sourceName + "-signed.pdf";
            destinationPath = Path.Combine(receiptFolder, signedName);
        }

        if (File.Exists(destinationPath) &&
            !string.IsNullOrWhiteSpace(journal.DestinationSha256))
        {
            var existingHash = await ComputeFileSha256Async(
                destinationPath,
                cancellationToken);
            if (!string.Equals(
                    existingHash,
                    journal.DestinationSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The saved signed pickup receipt no longer matches its recovery-journal SHA-256.");
            }

            if (!string.Equals(
                    existingHash,
                    validatedSourceHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The immutable signed pickup receipt does not match the PDF whose new signature was verified.");
            }

            return new VerifiedCopyResult(
                validatedSourceHash,
                existingHash,
                new FileInfo(destinationPath).Length);
        }

        var copy = await EnsureVerifiedCopyAsync(
            journal.SourcePdfPath,
            destinationPath,
            cancellationToken);
        if (!string.Equals(
                copy.SourceSha256,
                validatedSourceHash,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                copy.DestinationSha256,
                validatedSourceHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(
                "The pickup receipt changed between signature verification and immutable copy creation.");
        }
        await _journals.UpdateStageAsync(
            journal,
            "SignedPickupReceiptCopiedAndVerified",
            destinationPath: destinationPath,
            sourceHash: copy.SourceSha256,
            destinationHash: copy.DestinationSha256,
            cancellationToken: cancellationToken);
        return copy;
    }

    private async Task EnsureNoPendingPartialPickupAsync(
        string transactionId,
        CancellationToken cancellationToken)
    {
        var pending = await _journals.GetPendingAsync(cancellationToken);
        if (pending.Any(entry =>
                string.Equals(
                    entry.TransactionId,
                    transactionId,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    entry.OperationType,
                    WorkflowJournalService.PartialPickupOperation,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "This 1297 has a partial pickup in Recovery. Resume or roll it back before starting closeout.");
        }
    }

    private async Task EnsureNoPendingCloseoutOrPartialPickupAsync(
        string transactionId,
        CancellationToken cancellationToken)
    {
        var pending = await _journals.GetPendingAsync(cancellationToken);
        var blocking = pending.FirstOrDefault(entry =>
            string.Equals(
                entry.TransactionId,
                transactionId,
                StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(
                 entry.OperationType,
                 WorkflowJournalService.CloseoutOperation,
                 StringComparison.OrdinalIgnoreCase) ||
             string.Equals(
                 entry.OperationType,
                 WorkflowJournalService.PartialPickupOperation,
                 StringComparison.OrdinalIgnoreCase)));
        if (blocking is not null)
        {
            throw new InvalidOperationException(
                $"This 1297 already has a pending {blocking.DisplayName} workflow. Resume or roll it back from Recovery first.");
        }
    }

    private async Task RollbackPartialPickupAsync(
        WorkflowJournalEntry journal,
        CancellationToken cancellationToken)
    {
        var payload = journal.PartialPickup
            ?? throw new InvalidOperationException(
                "The partial-pickup recovery record does not contain its saved request.");
        var committed = await _database.GetPickupReceiptByIdAsync(
            payload.PickupId,
            cancellationToken);
        if (committed is not null)
        {
            throw new InvalidOperationException(
                "This signed partial-pickup receipt is already committed and cannot be rolled back.");
        }

        foreach (var path in new[]
                 {
                     journal.SourcePdfPath,
                     journal.DestinationPdfPath
                 }
                 .Where(path => !string.IsNullOrWhiteSpace(path))
                 .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
            {
                continue;
            }

            await WaitForStableExclusiveReadAsync(path, cancellationToken);
            var hash = await ComputeFileSha256Async(path, cancellationToken);
            var newSignature = await _signature.ExtractNewSignatureAsync(
                path,
                payload.ExistingSignatureFingerprints,
                cancellationToken);
            await TryRecordArtifactAsync(
                payload.TransactionId,
                newSignature.SignatureFound
                    ? "AbandonedSignedPartialPickupAttempt"
                    : "AbandonedPartialPickupAttempt",
                path,
                hash,
                new FileInfo(path).Length,
                cancellationToken);
        }

        await _journals.MarkAbandonedAsync(
            journal,
            "The technician abandoned the incomplete partial pickup. Its prepared or changed receipt files were preserved; no device status was committed.",
            cancellationToken);
        _logger.Workflow(
            payload.TransactionId,
            WorkflowJournalService.PartialPickupOperation,
            "Abandoned",
            $"Partial pickup {payload.PickupId} was abandoned without deleting its receipt evidence.");
    }

    private static void ValidatePartialPickupOperatorInput(
        string technician,
        string notes)
    {
        if (string.IsNullOrWhiteSpace(technician) ||
            technician.Trim().Length > 120)
        {
            throw new InvalidOperationException(
                "A valid technician name and grade is required for partial pickup.");
        }
        if ((notes?.Length ?? 0) > 1000)
        {
            throw new InvalidOperationException(
                "Partial-pickup notes cannot exceed 1000 characters.");
        }
    }

    private static void ValidatePartialPickupJournal(
        WorkflowJournalEntry journal,
        PendingPartialPickupPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.PickupId) ||
            !string.Equals(
                journal.OperationId,
                payload.PickupId,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(payload.TransactionId) ||
            !string.Equals(
                journal.TransactionId,
                payload.TransactionId,
                StringComparison.OrdinalIgnoreCase) ||
            payload.SequenceNumber <= 0 ||
            payload.DeviceIds.Count == 0 ||
            payload.DeviceIds.Any(id => id <= 0) ||
            payload.DeviceIds.Distinct().Count() != payload.DeviceIds.Count)
        {
            throw new InvalidDataException(
                "The partial-pickup recovery record contains invalid or inconsistent identifiers.");
        }

        ValidatePartialPickupOperatorInput(payload.Technician, payload.Notes);
        if (payload.ExistingSignatureFingerprints is null)
        {
            throw new InvalidDataException(
                "The partial-pickup signature baseline is missing.");
        }
    }
}
