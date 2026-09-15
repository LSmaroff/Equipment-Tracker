using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;

namespace EquipmentTracking.App.Services;

public sealed partial class TransactionWorkflowService
{
    private readonly AppPaths _paths;
    private readonly SettingsService _settings;
    private readonly PdfFormService _pdf;
    private readonly SignatureExtractionService _signature;
    private readonly DatabaseService _database;
    private readonly ExcelExportService _excel;
    private readonly FileNameService _fileNames;
    private readonly WorkflowJournalService _journals;
    private readonly FileLogger _logger;

    public TransactionWorkflowService(
        AppPaths paths,
        SettingsService settings,
        PdfFormService pdf,
        SignatureExtractionService signature,
        DatabaseService database,
        ExcelExportService excel,
        FileNameService fileNames,
        WorkflowJournalService journals,
        FileLogger logger)
    {
        _paths = paths;
        _settings = settings;
        _pdf = pdf;
        _signature = signature;
        _database = database;
        _excel = excel;
        _fileNames = fileNames;
        _journals = journals;
        _logger = logger;
    }

    public WorkingTransaction StartTransaction()
    {
        var templatePath = _settings.ResolvePath(_settings.Current.TemplatePdfPath);
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException(
                "The 1297 template was not found. Select it in Settings.",
                templatePath);
        }

        _paths.EnsureDirectories();
        var createdAt = DateTimeOffset.Now;
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var transactionId = $"TX-{createdAt:yyyyMMdd-HHmmss}-{suffix}";
        var workingFolder = Path.Combine(_paths.WorkingDirectory, transactionId);
        Directory.CreateDirectory(workingFolder);
        var templateCopy = Path.Combine(workingFolder, $"{transactionId}-TemplateCopy.pdf");
        var preparedPdf = Path.Combine(workingFolder, $"{transactionId}-Prepared.pdf");
        File.Copy(templatePath, templateCopy, overwrite: false);

        return new WorkingTransaction
        {
            TransactionId = transactionId,
            CreatedAt = createdAt,
            WorkingFolder = workingFolder,
            TemplateCopyPath = templateCopy,
            PreparedPdfPath = preparedPdf
        };
    }

    public PdfFillResult PreparePdf(
        WorkingTransaction working,
        CustomerIdentity customer,
        string phoneNumber,
        string technician,
        string organization,
        string ticketNumber,
        IReadOnlyList<DeviceRecord> devices)
    {
        ArgumentNullException.ThrowIfNull(working);
        ValidateTransactionInput(
            customer,
            phoneNumber,
            technician,
            organization,
            ticketNumber,
            devices);

        var maximumDevices = Math.Max(1, _settings.Current.MaximumDevicesPerForm);
        if (devices.Count > maximumDevices)
        {
            throw new InvalidOperationException(
                $"This form supports a maximum of {maximumDevices} devices.");
        }

        var values = PdfLogicalValueBuilder.Build(
            phoneNumber,
            customer.DisplayName,
            organization,
            ticketNumber,
            devices,
            issueDate: DateTimeOffset.Now);
        var result = _pdf.FillFields(
            working.TemplateCopyPath,
            working.PreparedPdfPath,
            values,
            _settings.Current,
            working.TransactionId);
        if (result.FieldsFilled == 0)
        {
            throw new InvalidOperationException(
                "No configured PDF fields were found in the 1297 template.");
        }

        var journal = _journals.CreateIntake(
            working,
            customer,
            phoneNumber,
            technician,
            organization,
            ticketNumber,
            devices);
        _journals.Save(journal);
        _logger.Workflow(
            working.TransactionId,
            WorkflowJournalService.IntakeOperation,
            journal.Stage,
            "Prepared the editable PDF and saved a restartable intake journal.");
        return result;
    }

    public async Task<FinalizationResult> FinalizeAsync(
        WorkingTransaction working,
        CustomerIdentity enteredCustomer,
        string phoneNumber,
        string technician,
        string organization,
        string ticketNumber,
        IReadOnlyList<DeviceRecord> devices,
        CacCertificateCandidate? selectedCertificate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(working);
        ValidateTransactionInput(
            enteredCustomer,
            phoneNumber,
            technician,
            organization,
            ticketNumber,
            devices);

        var journal = await GetOrCreateIntakeJournalAsync(
            working,
            enteredCustomer,
            phoneNumber,
            technician,
            organization,
            ticketNumber,
            devices,
            cancellationToken);

        try
        {
            if (!File.Exists(working.PreparedPdfPath))
            {
                throw new FileNotFoundException(
                    "The prepared PDF was not found.",
                    working.PreparedPdfPath);
            }

            await _journals.UpdateStageAsync(
                journal,
                "WaitingForStableSignedPdf",
                cancellationToken: cancellationToken);
            await WaitForStableExclusiveReadAsync(working.PreparedPdfPath, cancellationToken);

            _settings.Current.PdfFieldMappings.TryGetValue(
                "CustomerSignature",
                out var signatureField);
            signatureField = string.IsNullOrWhiteSpace(signatureField)
                ? "ISSUED TO SIGNATURE"
                : signatureField.Trim();

            var signature = await _signature.ExtractAsync(
                working.PreparedPdfPath,
                signatureField,
                cancellationToken: cancellationToken);
            if (_settings.Current.RequireSignatureForFinalization && !signature.SignatureFound)
            {
                var diagnostics = await _signature.DiagnoseAsync(
                    working.PreparedPdfPath,
                    signatureField,
                    cancellationToken: cancellationToken);
                throw new SignatureValidationException(
                    signature.DiagnosticMessage,
                    diagnostics);
            }

            await _journals.UpdateStageAsync(
                journal,
                "IntakeSignatureVerified",
                cancellationToken: cancellationToken);
            _logger.Workflow(
                working.TransactionId,
                WorkflowJournalService.IntakeOperation,
                journal.Stage,
                "The intake signature was read successfully.");

            // The intake fields are the authoritative customer record. A PDF signer
            // can be a different person, so keep that identity in the dedicated
            // signature/certificate metadata instead of replacing the customer.
            var customer = new CustomerIdentity
            {
                Rank = enteredCustomer.Rank,
                FirstName = enteredCustomer.FirstName,
                MiddleInitial = enteredCustomer.MiddleInitial,
                LastName = enteredCustomer.LastName,
                OriginalName = FirstNonEmpty(
                    enteredCustomer.OriginalName,
                    enteredCustomer.FullName)
            };
            var timestamp = signature.SigningTime ?? DateTimeOffset.Now;

            var existing = await _database.GetTransactionByIdAsync(
                working.TransactionId,
                cancellationToken);
            if (existing is not null)
            {
                // A previous attempt may have committed the database and then stopped
                // before artifact recording, export, or cleanup. Complete those stages
                // rather than attempting another insert.
                if (!File.Exists(existing.PdfPath) && File.Exists(working.PreparedPdfPath))
                {
                    await EnsureVerifiedCopyAsync(
                        working.PreparedPdfPath,
                        existing.PdfPath,
                        cancellationToken);
                }

                if (!string.IsNullOrWhiteSpace(journal.OriginalSignedPdfPath) &&
                    !File.Exists(journal.OriginalSignedPdfPath) &&
                    File.Exists(working.PreparedPdfPath))
                {
                    await EnsureVerifiedCopyAsync(
                        working.PreparedPdfPath,
                        journal.OriginalSignedPdfPath,
                        cancellationToken);
                }

                if (File.Exists(existing.PdfPath))
                {
                    var activeHash = await ComputeFileSha256Async(existing.PdfPath, cancellationToken);
                    await TryRecordArtifactAsync(
                        existing.Id,
                        "WorkingStatusCopy",
                        existing.PdfPath,
                        activeHash,
                        new FileInfo(existing.PdfPath).Length,
                        cancellationToken);
                }

                if (!string.IsNullOrWhiteSpace(journal.OriginalSignedPdfPath) &&
                    File.Exists(journal.OriginalSignedPdfPath))
                {
                    var originalHash = await ComputeFileSha256Async(
                        journal.OriginalSignedPdfPath,
                        cancellationToken);
                    await TryRecordArtifactAsync(
                        existing.Id,
                        "OriginalSignedIntake",
                        journal.OriginalSignedPdfPath,
                        originalHash,
                        new FileInfo(journal.OriginalSignedPdfPath).Length,
                        cancellationToken);
                }

                var resumedExport = await TryExportAsync(
                    working.TransactionId,
                    cancellationToken);
                await _journals.UpdateStageAsync(
                    journal,
                    "ResumedPostDatabaseStages",
                    destinationPath: existing.PdfPath,
                    cancellationToken: cancellationToken);
                TryDeleteWorkingFolder(working.WorkingFolder);
                await _journals.MarkCompletedAsync(journal, cancellationToken);
                return new FinalizationResult
                {
                    Transaction = existing,
                    Signature = signature,
                    FinalPdfPath = existing.PdfPath,
                    ExcelExportPath = resumedExport
                };
            }

            var completedFolder = _settings.ResolvePath(
                _settings.Current.CompletedPdfFolder);
            if (string.IsNullOrWhiteSpace(completedFolder))
            {
                throw new InvalidOperationException(
                    "The completed-PDF folder is not configured.");
            }

            Directory.CreateDirectory(completedFolder);
            var finalName = _fileNames.BuildFinalPdfName(
                customer,
                timestamp,
                working.TransactionId);
            var activePdfPath = string.IsNullOrWhiteSpace(journal.DestinationPdfPath)
                ? _fileNames.EnsureUniquePath(completedFolder, finalName)
                : journal.DestinationPdfPath;

            var originalFolder = Path.Combine(
                completedFolder,
                "Original Signed Intake",
                _fileNames.NormalizeDirectoryName(organization));
            Directory.CreateDirectory(originalFolder);
            var originalFileName =
                Path.GetFileNameWithoutExtension(finalName) +
                "-original-signed-intake.pdf";
            var originalPdfPath = string.IsNullOrWhiteSpace(journal.OriginalSignedPdfPath)
                ? _fileNames.EnsureUniquePath(originalFolder, originalFileName)
                : journal.OriginalSignedPdfPath;
            journal.DestinationPdfPath = activePdfPath;
            journal.OriginalSignedPdfPath = originalPdfPath;
            await _journals.SaveAsync(journal, cancellationToken);

            var originalCopy = await EnsureVerifiedCopyAsync(
                working.PreparedPdfPath,
                originalPdfPath,
                cancellationToken);
            await _journals.UpdateStageAsync(
                journal,
                "OriginalSignedIntakePreserved",
                destinationPath: activePdfPath,
                sourceHash: originalCopy.SourceSha256,
                destinationHash: originalCopy.DestinationSha256,
                cancellationToken: cancellationToken);

            var activeCopy = await EnsureVerifiedCopyAsync(
                working.PreparedPdfPath,
                activePdfPath,
                cancellationToken);
            await _journals.UpdateStageAsync(
                journal,
                "WorkingStatusCopyCreated",
                destinationPath: activePdfPath,
                sourceHash: activeCopy.SourceSha256,
                destinationHash: activeCopy.DestinationSha256,
                cancellationToken: cancellationToken);

            var transaction = new EquipmentTransaction
            {
                Id = working.TransactionId,
                Customer = customer,
                PhoneNumber = phoneNumber.Trim(),
                Technician = technician.Trim(),
                Organization = organization.Trim(),
                TicketNumber = ticketNumber.Trim(),
                IssuedAt = timestamp,
                CreatedAt = working.CreatedAt,
                PdfPath = activePdfPath,
                PdfSignerName = signature.SignerName,
                CertificateSubject = FirstNonEmpty(
                    signature.CertificateSubject,
                    selectedCertificate?.Subject),
                CertificateThumbprint = FirstNonEmpty(
                    signature.CertificateThumbprint,
                    selectedCertificate?.Thumbprint),
                SignatureTime = signature.SigningTime,
                Devices = devices.Select(CloneIssuedDevice).ToList()
            };

            if (!await _database.InsertTransactionAsync(transaction, cancellationToken))
            {
                transaction = await _database.GetTransactionByIdAsync(
                    working.TransactionId,
                    cancellationToken)
                    ?? throw new InvalidOperationException(
                        "The existing transaction could not be loaded.");
                activePdfPath = transaction.PdfPath;
            }

            await _journals.UpdateStageAsync(
                journal,
                "DatabaseUpdated",
                destinationPath: activePdfPath,
                cancellationToken: cancellationToken);
            await TryRecordArtifactAsync(
                transaction.Id,
                "OriginalSignedIntake",
                originalPdfPath,
                originalCopy.DestinationSha256,
                originalCopy.SizeBytes,
                cancellationToken);
            await TryRecordArtifactAsync(
                transaction.Id,
                "WorkingStatusCopy",
                activePdfPath,
                activeCopy.DestinationSha256,
                activeCopy.SizeBytes,
                cancellationToken);

            var excelPath = await TryExportAsync(
                working.TransactionId,
                cancellationToken);
            await _journals.UpdateStageAsync(
                journal,
                "ExcelRefreshed",
                cancellationToken: cancellationToken);
            TryDeleteWorkingFolder(working.WorkingFolder);
            await _journals.MarkCompletedAsync(journal, cancellationToken);
            _logger.Workflow(
                working.TransactionId,
                WorkflowJournalService.IntakeOperation,
                "Completed",
                "Intake finalization completed successfully.");

            return new FinalizationResult
            {
                Transaction = transaction,
                Signature = signature,
                FinalPdfPath = activePdfPath,
                ExcelExportPath = excelPath
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
                working.TransactionId,
                WorkflowJournalService.IntakeOperation,
                journal.Stage,
                "Intake finalization failed and can be resumed from Recovery. " +
                BuildWorkflowFailureContext(journal, ticketNumber),
                ex);
            throw;
        }
    }

    public Task<IReadOnlyCollection<string>> CaptureSignatureBaselineAsync(
        string pdfPath,
        CancellationToken cancellationToken = default)
    {
        return _signature.GetSignatureFingerprintsAsync(pdfPath, cancellationToken);
    }

    public async Task<CloseoutPreparationResult> BeginCloseoutAsync(
        EquipmentTransaction transaction,
        DateTimeOffset closeoutStartedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.IsArchived)
        {
            throw new InvalidOperationException("This 1297 is already archived.");
        }
        if (!File.Exists(transaction.PdfPath))
        {
            throw new FileNotFoundException(
                "The selected 1297 PDF was not found.",
                transaction.PdfPath);
        }

        await EnsureNoPendingPartialPickupAsync(transaction.Id, cancellationToken);

        var operationId = $"closeout-{transaction.Id}";
        var existingJournal = await _journals.GetAsync(operationId, cancellationToken);
        if (existingJournal is not null &&
            !string.Equals(existingJournal.Status, WorkflowJournalService.CompletedStatus, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(existingJournal.Status, WorkflowJournalService.AbandonedStatus, StringComparison.OrdinalIgnoreCase) &&
            existingJournal.Closeout is not null)
        {
            _logger.Workflow(
                transaction.Id,
                WorkflowJournalService.CloseoutOperation,
                existingJournal.Stage,
                "Reopened the existing closeout attempt without replacing its original signature baseline.");
            return new CloseoutPreparationResult
            {
                Transaction = transaction,
                SignatureSearchStartedAt = existingJournal.Closeout.SignatureSearchStartedAt,
                ExistingSignatureFingerprints = existingJournal.Closeout.ExistingSignatureFingerprints,
                Journal = existingJournal
            };
        }

        await WaitForStableExclusiveReadAsync(transaction.PdfPath, cancellationToken);

        // The cleaned production template has no JavaScript. Populate RETURN DATE
        // before the customer signs Pickup Signature so the signed revision already
        // contains the final date.
        _pdf.UpdateLogicalFieldsInPlace(
            transaction.PdfPath,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ReturnDate"] = closeoutStartedAt.ToLocalTime().ToString(
                    "MM/dd/yyyy",
                    CultureInfo.InvariantCulture)
            },
            _settings.Current);
        await WaitForStableExclusiveReadAsync(transaction.PdfPath, cancellationToken);

        var fingerprints = await _signature.GetSignatureFingerprintsAsync(
            transaction.PdfPath,
            cancellationToken);
        var journal = _journals.CreateCloseout(
            transaction,
            closeoutStartedAt,
            fingerprints);
        journal.SourceSha256 = await ComputeFileSha256Async(
            transaction.PdfPath,
            cancellationToken);
        await _journals.SaveAsync(journal, cancellationToken);
        await TryRecordArtifactAsync(
            transaction.Id,
            "WorkingStatusCopyPreparedForCloseout",
            transaction.PdfPath,
            journal.SourceSha256,
            new FileInfo(transaction.PdfPath).Length,
            cancellationToken);
        _logger.Workflow(
            transaction.Id,
            WorkflowJournalService.CloseoutOperation,
            journal.Stage,
            "Return date was populated, the existing signature baseline was captured, and Adobe can now be opened.");

        return new CloseoutPreparationResult
        {
            Transaction = transaction,
            SignatureSearchStartedAt = closeoutStartedAt,
            ExistingSignatureFingerprints = fingerprints,
            Journal = journal
        };
    }

    public async Task PrepareCloseoutAsync(
        EquipmentTransaction transaction,
        string technician,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.IsArchived)
        {
            throw new InvalidOperationException("This 1297 is already archived.");
        }
        if (!File.Exists(transaction.PdfPath))
        {
            throw new FileNotFoundException(
                "The selected 1297 PDF was not found.",
                transaction.PdfPath);
        }

        var journal = await _journals.GetAsync(
            $"closeout-{transaction.Id}",
            cancellationToken);
        if (journal?.Closeout is not null)
        {
            journal.Closeout.Technician = technician.Trim();
            journal.Closeout.ClosedAt = DateTimeOffset.Now;
            await _journals.UpdateStageAsync(
                journal,
                "CloseoutConfirmedByTechnician",
                cancellationToken: cancellationToken);
        }

        await _database.MarkCloseoutPreparedAsync(
            transaction.Id,
            technician,
            cancellationToken);
    }

    public async Task<CloseoutResult> FinalizeCloseoutAsync(
        CloseoutRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Technician))
        {
            throw new InvalidOperationException(
                "Enter the technician completing the closeout.");
        }

        var journal = await GetOrCreateCloseoutJournalAsync(request, cancellationToken);
        try
        {
            var currentTransaction = await _database.GetTransactionByIdAsync(
                request.Transaction.Id,
                cancellationToken)
                ?? throw new InvalidOperationException(
                    "The selected transaction could not be reloaded.");

            if (currentTransaction.IsArchived)
            {
                var existingSignature = await _signature.ExtractAsync(
                    currentTransaction.PdfPath,
                    AppSettings.PickupSignatureFieldName,
                    preferredFieldOnly: false,
                    cancellationToken: cancellationToken);
                await _journals.MarkCompletedAsync(journal, cancellationToken);
                return new CloseoutResult
                {
                    Transaction = currentTransaction,
                    ArchivedPdfPath = currentTransaction.PdfPath,
                    ExcelExportPath = await TryExportAsync(currentTransaction.Id, cancellationToken),
                    PickupSignature = existingSignature
                };
            }

            await _journals.UpdateStageAsync(
                journal,
                "WaitingForStablePickupSignedPdf",
                cancellationToken: cancellationToken);
            await WaitForStableExclusiveReadAsync(
                currentTransaction.PdfPath,
                cancellationToken);

            const string pickupField = AppSettings.PickupSignatureFieldName;
            var pickupSignature = await _signature.ExtractAsync(
                currentTransaction.PdfPath,
                pickupField,
                preferredFieldOnly: true,
                cancellationToken: cancellationToken);
            if (!pickupSignature.SignatureFound)
            {
                pickupSignature = await TryFindCloseoutSignatureFallbackAsync(
                    currentTransaction.PdfPath,
                    pickupField,
                    request.ExistingSignatureFingerprints,
                    request.SignatureSearchStartedAt,
                    pickupSignature.DiagnosticMessage,
                    cancellationToken);
            }

            await _journals.UpdateStageAsync(
                journal,
                "PickupSignatureVerified",
                cancellationToken: cancellationToken);

            var completedRoot = _settings.ResolvePath(
                _settings.Current.CompletedPdfFolder);
            if (string.IsNullOrWhiteSpace(completedRoot))
            {
                throw new InvalidOperationException(
                    "The completed-PDF folder is not configured.");
            }

            var archiveRoot = Path.Combine(completedRoot, "Archived 1297s");
            var organizationFolder = Path.Combine(
                archiveRoot,
                _fileNames.NormalizeDirectoryName(currentTransaction.Organization));
            Directory.CreateDirectory(organizationFolder);
            var archivedName = _fileNames.BuildClosedPdfName(
                currentTransaction.PdfPath,
                request.ClosedAt);
            var archivedPath = string.IsNullOrWhiteSpace(journal.DestinationPdfPath)
                ? _fileNames.EnsureUniquePath(organizationFolder, archivedName)
                : journal.DestinationPdfPath;
            journal.DestinationPdfPath = archivedPath;
            if (journal.Closeout is not null)
            {
                journal.Closeout.Technician = request.Technician.Trim();
                journal.Closeout.ClosedAt = request.ClosedAt;
            }
            await _journals.SaveAsync(journal, cancellationToken);

            var sourcePath = Path.GetFullPath(currentTransaction.PdfPath);
            var destinationPath = Path.GetFullPath(archivedPath);
            var verifiedCopy = await EnsureVerifiedCopyAsync(
                sourcePath,
                destinationPath,
                cancellationToken);
            await _journals.UpdateStageAsync(
                journal,
                "FinalCloseoutPdfCopiedAndVerified",
                destinationPath,
                verifiedCopy.SourceSha256,
                verifiedCopy.DestinationSha256,
                cancellationToken);

            await _database.ArchiveTransactionAsync(
                currentTransaction.Id,
                destinationPath,
                request.Technician,
                request.ClosedAt,
                cancellationToken);
            await _journals.UpdateStageAsync(
                journal,
                "DatabaseArchived",
                destinationPath: destinationPath,
                cancellationToken: cancellationToken);
            await TryRecordArtifactAsync(
                currentTransaction.Id,
                "FinalSignedCloseout",
                destinationPath,
                verifiedCopy.DestinationSha256,
                verifiedCopy.SizeBytes,
                cancellationToken);

            if (!string.Equals(
                    sourcePath,
                    destinationPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteWithLogging(
                    sourcePath,
                    currentTransaction.Id,
                    "remove active working copy after archive");
            }

            var archived = await _database.GetTransactionByIdAsync(
                currentTransaction.Id,
                cancellationToken)
                ?? throw new InvalidOperationException(
                    "The archived transaction could not be reloaded.");
            var excelPath = await TryExportAsync(
                currentTransaction.Id,
                cancellationToken);
            await _journals.UpdateStageAsync(
                journal,
                "ExcelRefreshed",
                cancellationToken: cancellationToken);
            await _journals.MarkCompletedAsync(journal, cancellationToken);
            _logger.Workflow(
                currentTransaction.Id,
                WorkflowJournalService.CloseoutOperation,
                "Completed",
                "Closeout completed and the final signed PDF was archived.");

            return new CloseoutResult
            {
                Transaction = archived,
                ArchivedPdfPath = destinationPath,
                ExcelExportPath = excelPath,
                PickupSignature = pickupSignature
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
                request.Transaction.Id,
                WorkflowJournalService.CloseoutOperation,
                journal.Stage,
                "Closeout failed and can be resumed from Recovery. " +
                BuildWorkflowFailureContext(journal, request.Transaction.TicketNumber),
                ex);
            throw;
        }
    }

    public async Task<WorkflowRecoveryResult> ResumeWorkflowAsync(
        WorkflowJournalEntry journal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (string.Equals(
                journal.OperationType,
                WorkflowJournalService.IntakeOperation,
                StringComparison.OrdinalIgnoreCase))
        {
            var payload = journal.Intake
                ?? throw new InvalidOperationException(
                    "The intake recovery record does not contain its saved transaction data.");
            foreach (var device in payload.Devices.Where(device =>
                         string.IsNullOrWhiteSpace(device.PartNumber)))
            {
                // Journals created before schema 5 stored the part number in Model.
                device.PartNumber = device.Model;
            }
            var result = await FinalizeAsync(
                payload.Working,
                payload.Customer,
                payload.PhoneNumber,
                payload.Technician,
                payload.Organization,
                payload.TicketNumber,
                payload.Devices,
                selectedCertificate: null,
                cancellationToken);
            return new WorkflowRecoveryResult
            {
                OperationId = journal.OperationId,
                TransactionId = result.Transaction.Id,
                Message = "The interrupted intake transaction was completed.",
                PdfPath = result.FinalPdfPath,
                Completed = true
            };
        }

        if (string.Equals(
                journal.OperationType,
                WorkflowJournalService.CloseoutOperation,
                StringComparison.OrdinalIgnoreCase))
        {
            var payload = journal.Closeout
                ?? throw new InvalidOperationException(
                    "The closeout recovery record does not contain its saved closeout data.");
            var transaction = await _database.GetTransactionByIdAsync(
                payload.TransactionId,
                cancellationToken)
                ?? throw new InvalidOperationException(
                    "The transaction referenced by the closeout recovery record no longer exists.");
            if (string.IsNullOrWhiteSpace(payload.Technician))
            {
                throw new InvalidOperationException(
                    "The interrupted closeout was saved before a technician was selected. Start closeout again from Dashboard or abandon this recovery item.");
            }

            var result = await FinalizeCloseoutAsync(new CloseoutRequest
            {
                Transaction = transaction,
                Technician = payload.Technician,
                ClosedAt = payload.ClosedAt,
                SignatureSearchStartedAt = payload.SignatureSearchStartedAt,
                ExistingSignatureFingerprints = payload.ExistingSignatureFingerprints
            }, cancellationToken);
            return new WorkflowRecoveryResult
            {
                OperationId = journal.OperationId,
                TransactionId = result.Transaction.Id,
                Message = "The interrupted closeout was completed.",
                PdfPath = result.ArchivedPdfPath,
                Completed = true
            };
        }

        if (string.Equals(
                journal.OperationType,
                WorkflowJournalService.PartialPickupOperation,
                StringComparison.OrdinalIgnoreCase))
        {
            var result = await FinalizePartialPickupAsync(
                journal.OperationId,
                cancellationToken);
            return new WorkflowRecoveryResult
            {
                OperationId = journal.OperationId,
                TransactionId = result.Transaction.Id,
                Message = result.Archived
                    ? "The interrupted final pickup was completed and the parent 1297 was archived."
                    : $"The interrupted partial pickup was completed; {result.RemainingCount} device(s) remain outstanding.",
                PdfPath = result.PickupDocument.PdfPath,
                Completed = true
            };
        }

        throw new InvalidOperationException(
            $"Unsupported recovery operation type '{journal.OperationType}'.");
    }

    public async Task RollbackWorkflowAsync(
        WorkflowJournalEntry journal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);
        var transaction = await _database.GetTransactionByIdAsync(
            journal.TransactionId,
            cancellationToken);

        if (string.Equals(
                journal.OperationType,
                WorkflowJournalService.IntakeOperation,
                StringComparison.OrdinalIgnoreCase))
        {
            if (transaction is not null)
            {
                throw new InvalidOperationException(
                    "This intake already exists in the database and cannot be rolled back. Resume it so the remaining export and cleanup stages can finish.");
            }

            if (journal.Intake is not null)
            {
                TryDeleteWorkingFolder(journal.Intake.Working.WorkingFolder);
            }
            TryDelete(journal.DestinationPdfPath);
            TryDelete(journal.OriginalSignedPdfPath);
            await _journals.MarkAbandonedAsync(
                journal,
                "The technician rolled back the incomplete intake before it was committed to the database.",
                cancellationToken);
            return;
        }

        if (string.Equals(
                journal.OperationType,
                WorkflowJournalService.CloseoutOperation,
                StringComparison.OrdinalIgnoreCase))
        {
            if (transaction?.IsArchived == true)
            {
                throw new InvalidOperationException(
                    "This closeout is already archived and cannot be rolled back.");
            }

            if (transaction is not null && File.Exists(transaction.PdfPath))
            {
                await PreserveAbandonedCloseoutAttemptIfChangedAsync(
                    transaction,
                    journal,
                    cancellationToken);

                _pdf.UpdateLogicalFieldsInPlace(
                    transaction.PdfPath,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ReturnDate"] = string.Empty
                    },
                    _settings.Current);
            }
            if (!string.Equals(
                    journal.SourcePdfPath,
                    journal.DestinationPdfPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(journal.DestinationPdfPath);
            }
            if (transaction is not null)
            {
                await _database.ClearCloseoutPreparedAsync(
                    transaction.Id,
                    cancellationToken);
            }
            await _journals.MarkAbandonedAsync(
                journal,
                "The technician abandoned the incomplete closeout. The active 1297 remains available.",
                cancellationToken);
            return;
        }

        if (string.Equals(
                journal.OperationType,
                WorkflowJournalService.PartialPickupOperation,
                StringComparison.OrdinalIgnoreCase))
        {
            await RollbackPartialPickupAsync(journal, cancellationToken);
            return;
        }

        throw new InvalidOperationException(
            $"Unsupported recovery operation type '{journal.OperationType}'.");
    }

    public Task MarkWorkflowAbandonedAsync(
        WorkflowJournalEntry journal,
        string reason,
        CancellationToken cancellationToken = default)
    {
        return _journals.MarkAbandonedAsync(journal, reason, cancellationToken);
    }

    private async Task PreserveAbandonedCloseoutAttemptIfChangedAsync(
        EquipmentTransaction transaction,
        WorkflowJournalEntry journal,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(journal.SourceSha256))
        {
            return;
        }

        var currentHash = await ComputeFileSha256Async(
            transaction.PdfPath,
            cancellationToken);
        if (string.Equals(
                currentHash,
                journal.SourceSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var completedRoot = _settings.ResolvePath(
            _settings.Current.CompletedPdfFolder);
        if (string.IsNullOrWhiteSpace(completedRoot))
        {
            throw new InvalidOperationException(
                "The changed closeout attempt cannot be preserved because the completed-PDF folder is not configured.");
        }

        var organizationFolder = Path.Combine(
            completedRoot,
            "Abandoned Closeout Attempts",
            _fileNames.NormalizeDirectoryName(transaction.Organization));
        Directory.CreateDirectory(organizationFolder);
        var fileName =
            Path.GetFileNameWithoutExtension(transaction.PdfPath) +
            $"-abandoned-closeout-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.pdf";
        var preservedPath = _fileNames.EnsureUniquePath(
            organizationFolder,
            fileName);
        var copy = await EnsureVerifiedCopyAsync(
            transaction.PdfPath,
            preservedPath,
            cancellationToken);

        var artifactType = "AbandonedCloseoutAttempt";
        if (journal.Closeout?.ExistingSignatureFingerprints is { Count: > 0 } baseline)
        {
            var newSignature = await _signature.ExtractNewSignatureAsync(
                transaction.PdfPath,
                baseline,
                cancellationToken);
            if (newSignature.SignatureFound)
            {
                artifactType = "AbandonedSignedCloseoutAttempt";
            }
        }

        await TryRecordArtifactAsync(
            transaction.Id,
            artifactType,
            preservedPath,
            copy.DestinationSha256,
            copy.SizeBytes,
            cancellationToken);
        _logger.Workflow(
            transaction.Id,
            WorkflowJournalService.CloseoutOperation,
            "RollbackPreservation",
            $"Preserved the changed closeout attempt before rollback: {preservedPath}");
    }

    private async Task<WorkflowJournalEntry> GetOrCreateIntakeJournalAsync(
        WorkingTransaction working,
        CustomerIdentity customer,
        string phoneNumber,
        string technician,
        string organization,
        string ticketNumber,
        IReadOnlyList<DeviceRecord> devices,
        CancellationToken cancellationToken)
    {
        var operationId = $"intake-{working.TransactionId}";
        var journal = await _journals.GetAsync(operationId, cancellationToken);
        if (journal is not null)
        {
            return journal;
        }

        journal = _journals.CreateIntake(
            working,
            customer,
            phoneNumber,
            technician,
            organization,
            ticketNumber,
            devices);
        await _journals.SaveAsync(journal, cancellationToken);
        return journal;
    }

    private async Task<WorkflowJournalEntry> GetOrCreateCloseoutJournalAsync(
        CloseoutRequest request,
        CancellationToken cancellationToken)
    {
        var operationId = $"closeout-{request.Transaction.Id}";
        var journal = await _journals.GetAsync(operationId, cancellationToken);
        if (journal is null)
        {
            journal = _journals.CreateCloseout(
                request.Transaction,
                request.SignatureSearchStartedAt,
                request.ExistingSignatureFingerprints ?? []);
        }

        journal.Closeout ??= new PendingCloseoutPayload
        {
            TransactionId = request.Transaction.Id,
            SignatureSearchStartedAt = request.SignatureSearchStartedAt,
            ExistingSignatureFingerprints =
                request.ExistingSignatureFingerprints?.ToList() ?? []
        };
        journal.Closeout.Technician = request.Technician.Trim();
        journal.Closeout.ClosedAt = request.ClosedAt;
        await _journals.SaveAsync(journal, cancellationToken);
        return journal;
    }

    private async Task<SignatureInfo> TryFindCloseoutSignatureFallbackAsync(
        string pdfPath,
        string pickupField,
        IReadOnlyCollection<string>? existingSignatureFingerprints,
        DateTimeOffset signatureSearchStartedAt,
        string fieldSpecificDiagnostic,
        CancellationToken cancellationToken)
    {
        if (existingSignatureFingerprints is not null)
        {
            var newlyAddedSignature = await _signature.ExtractNewSignatureAsync(
                pdfPath,
                existingSignatureFingerprints,
                cancellationToken);
            if (newlyAddedSignature.SignatureFound)
            {
                _logger.Warning(
                    $"The exact '{pickupField}' field lookup could not read the signature through raw PDF objects. " +
                    "A new CMS/PKCS#7 signature added after closeout started was detected and accepted instead.");
                return newlyAddedSignature;
            }

            var diagnostics = await _signature.DiagnoseAsync(
                pdfPath,
                pickupField,
                existingSignatureFingerprints,
                signatureSearchStartedAt,
                cancellationToken);
            throw new SignatureValidationException(
                fieldSpecificDiagnostic + Environment.NewLine + Environment.NewLine +
                newlyAddedSignature.DiagnosticMessage,
                diagnostics);
        }

        var fallbackSignature = await _signature.ExtractAsync(
            pdfPath,
            pickupField,
            preferredFieldOnly: false,
            cancellationToken: cancellationToken);
        var earliestAcceptedSigningTime = signatureSearchStartedAt.AddMinutes(-5);
        if (!fallbackSignature.SignatureFound ||
            fallbackSignature.SigningTime is null ||
            fallbackSignature.SigningTime < earliestAcceptedSigningTime)
        {
            var diagnostics = await _signature.DiagnoseAsync(
                pdfPath,
                pickupField,
                existingSignatureFingerprints,
                signatureSearchStartedAt,
                cancellationToken);
            throw new SignatureValidationException(
                fieldSpecificDiagnostic + Environment.NewLine + Environment.NewLine +
                "A signature exists somewhere in the PDF, but it could not be tied to the exact 'Pickup Signature' field and was not proven to be newly added during this closeout attempt.",
                diagnostics);
        }

        _logger.Warning(
            $"The exact '{pickupField}' field lookup did not locate a readable signature. " +
            "A newer CMS/PKCS#7 signature was accepted based on its signing time because no baseline was available.");
        return fallbackSignature;
    }

    private async Task<string> TryExportAsync(
        string transactionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _excel.ExportAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"Transaction {transactionId} was saved, but the Excel report was not updated.",
                ex);
            return string.Empty;
        }
    }

    private async Task TryRecordArtifactAsync(
        string transactionId,
        string artifactType,
        string path,
        string sha256,
        long sizeBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            await _database.RecordFileArtifactAsync(
                transactionId,
                artifactType,
                path,
                sha256,
                sizeBytes,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"File artifact metadata could not be recorded for transaction {transactionId}.",
                ex);
        }
    }

    private async Task TryMarkJournalFailedAsync(
        WorkflowJournalEntry journal,
        string stage,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            await _journals.MarkFailedAsync(
                journal,
                stage,
                exception,
                cancellationToken);
        }
        catch (Exception journalException)
        {
            _logger.Error(
                "The workflow failed and its recovery journal could not be updated.",
                journalException);
        }
    }

    private static void ValidateTransactionInput(
        CustomerIdentity customer,
        string phoneNumber,
        string technician,
        string organization,
        string ticketNumber,
        IReadOnlyList<DeviceRecord> devices)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(devices);
        if (!customer.HasUsableName)
            throw new InvalidOperationException("Customer first and last name are required.");
        ValidateLength(customer.Rank, 32, "Customer rank");
        ValidateLength(customer.FirstName, 80, "Customer first name");
        ValidateLength(customer.MiddleInitial, 1, "Customer middle initial");
        ValidateLength(customer.LastName, 80, "Customer last name");
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new InvalidOperationException("A duty phone number is required.");
        ValidateLength(phoneNumber, 40, "Duty phone number");
        if (string.IsNullOrWhiteSpace(technician))
            throw new InvalidOperationException("Technician name and grade are required.");
        ValidateLength(technician, 120, "Technician name and grade");
        if (string.IsNullOrWhiteSpace(organization))
            throw new InvalidOperationException("Select an organization.");
        ValidateLength(organization, 120, "Organization");
        if (string.IsNullOrWhiteSpace(ticketNumber))
            throw new InvalidOperationException("A ticket number is required.");
        ValidateLength(ticketNumber, 80, "Ticket number");
        if (devices.Count == 0)
            throw new InvalidOperationException("Add at least one device.");
        if (devices.Any(device =>
                string.IsNullOrWhiteSpace(device.PartNumber) ||
                string.IsNullOrWhiteSpace(device.SerialNumber)))
            throw new InvalidOperationException(
                "Every device must have a part number and serial number.");
        foreach (var device in devices)
        {
            ValidateLength(device.PartNumber, 128, "Device part number");
            ValidateLength(device.Model, 128, "Device model name");
            ValidateLength(device.SerialNumber, 128, "Device serial number");
            ValidateLength(device.AssetTag, 128, "Device asset tag");
            ValidateLength(device.RawScanValue, 4096, "Raw scan value");
        }
        var duplicate = devices
            .GroupBy(device => device.SerialNumber, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Serial number '{duplicate.Key}' appears more than once.");
    }

    private static DeviceRecord CloneIssuedDevice(DeviceRecord device) => new()
    {
        PartNumber = string.IsNullOrWhiteSpace(device.PartNumber)
            ? device.Model
            : device.PartNumber,
        Model = device.Model,
        SerialNumber = device.SerialNumber,
        AssetTag = device.AssetTag,
        RawScanValue = device.RawScanValue,
        Status = DeviceStatusCatalog.InShop
    };

    private static async Task<VerifiedCopyResult> EnsureVerifiedCopyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (File.Exists(destinationPath))
        {
            var sourceHash = await ComputeFileSha256Async(sourcePath, cancellationToken);
            var destinationHash = await ComputeFileSha256Async(destinationPath, cancellationToken);
            if (!string.Equals(
                    sourceHash,
                    destinationHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    $"A recovery destination already exists but does not match the source PDF: {destinationPath}");
            }

            return new VerifiedCopyResult(
                sourceHash,
                destinationHash,
                new FileInfo(destinationPath).Length);
        }

        return await CopyFileVerifiedAsync(
            sourcePath,
            destinationPath,
            cancellationToken);
    }

    private static async Task<VerifiedCopyResult> CopyFileVerifiedAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("The destination PDF folder is invalid.");
        Directory.CreateDirectory(destinationDirectory);
        var temporaryPath = destinationPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using var sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long bytesCopied = 0;
            await using (var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[64 * 1024];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    bytesCopied += read;
                    sourceHash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(
                        buffer.AsMemory(0, read),
                        cancellationToken);
                }

                await destination.FlushAsync(cancellationToken);
                destination.Flush(flushToDisk: true);
            }

            var expectedHash = sourceHash.GetHashAndReset();
            byte[] actualHash;
            await using (var verificationStream = new FileStream(
                temporaryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                actualHash = await SHA256.HashDataAsync(
                    verificationStream,
                    cancellationToken);
            }

            if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
            {
                throw new IOException(
                    "The PDF copy failed SHA-256 integrity verification.");
            }

            await MoveFileWithRetryAsync(
                temporaryPath,
                destinationPath,
                cancellationToken);
            return new VerifiedCopyResult(
                Convert.ToHexString(expectedHash),
                Convert.ToHexString(actualHash),
                bytesCopied);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private static async Task MoveFileWithRetryAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        IOException? lastException = null;
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Move(sourcePath, destinationPath, overwrite: false);
                return;
            }
            catch (IOException ex)
            {
                lastException = ex;
                if (attempt < 8)
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(250 * attempt),
                        cancellationToken);
                }
            }
        }

        throw new IOException(
            $"The verified PDF copy could not be committed to '{destinationPath}'. " +
            $"Temporary file: '{sourcePath}'. " +
            "The rename was attempted 8 times. " +
            $"Windows reported: {lastException?.Message ?? "an unknown file-system error"}",
            lastException);
    }

    private static async Task WaitForStableExclusiveReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "The selected PDF was not found.",
                path);
        }

        long? previousLength = null;
        DateTime? previousWriteTime = null;
        var stableSamples = 0;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None);
                var length = stream.Length;
                var writeTime = File.GetLastWriteTimeUtc(path);
                if (length > 0 &&
                    previousLength == length &&
                    previousWriteTime == writeTime)
                {
                    stableSamples++;
                    if (stableSamples >= 2)
                    {
                        return;
                    }
                }
                else
                {
                    stableSamples = 0;
                }

                previousLength = length;
                previousWriteTime = writeTime;
            }
            catch (IOException)
            {
                stableSamples = 0;
                previousLength = null;
                previousWriteTime = null;
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new IOException(
            "The PDF is still open, changing, or has not finished saving after 40 checks over approximately 20 seconds. " +
            "Save it, close Adobe, wait a few seconds, and try again.");
    }

    private static string BuildWorkflowFailureContext(
        WorkflowJournalEntry journal,
        string? ticketNumber)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
            ?? "unknown";
        return
            $"AppVersion={version}; " +
            $"Ticket={ticketNumber?.Trim() ?? string.Empty}; " +
            $"RetryCount={journal.RetryCount}; " +
            $"AdobeProcesses={GetAdobeProcessSummary()}; " +
            $"SourcePath={journal.SourcePdfPath}; " +
            $"DestinationPath={journal.DestinationPdfPath}.";
    }

    private static string GetAdobeProcessSummary()
    {
        var processNames = new[] { "Acrobat", "AcroRd32", "AcroCEF", "RdrCEF" };
        try
        {
            var running = new List<string>();
            foreach (var processName in processNames)
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    using (process)
                    {
                        running.Add($"{process.ProcessName}:{process.Id}");
                    }
                }
            }

            var summary = running
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return summary.Length == 0 ? "none" : string.Join(",", summary);
        }
        catch (Exception ex)
        {
            return $"unavailable({ex.GetType().Name})";
        }
    }

    private static async Task<string> ComputeFileSha256Async(
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
        return Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static void ValidateLength(
        string? value,
        int maximumLength,
        string fieldName)
    {
        if ((value?.Length ?? 0) > maximumLength)
        {
            throw new InvalidOperationException(
                $"{fieldName} cannot exceed {maximumLength} characters.");
        }
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
        ?? string.Empty;

    private void TryDeleteWithLogging(
        string path,
        string transactionId,
        string operation)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"Transaction {transactionId}: could not {operation}. The verified archived copy and database record were preserved. Path: {path}",
                ex);
        }
    }

    private static void TryDelete(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static void TryDeleteWorkingFolder(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private sealed record VerifiedCopyResult(
        string SourceSha256,
        string DestinationSha256,
        long SizeBytes);
}
