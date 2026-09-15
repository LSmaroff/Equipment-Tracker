using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using Microsoft.Data.Sqlite;

namespace EquipmentTracking.App.Services;

public sealed class DatabaseService : IDeviceRecognitionDataSource
{
    public const int CurrentSchemaVersion = 6;
    public const string SignedPartialPickupArtifactType = "SignedPartialPickup";

    private readonly AppPaths _paths;
    private readonly FileLogger _logger;
    private readonly BackupService _backupService;
    private readonly string _connectionString;

    public DatabaseService(AppPaths paths, FileLogger logger, BackupService backupService)
    {
        _paths = paths;
        _logger = logger;
        _backupService = backupService;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = true
        }.ToString();
    }

    public async Task InitializeAsync(
        bool createAutomaticBackup = true,
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureDirectories();
        var databaseExistedBeforeStartup = File.Exists(_paths.DatabasePath);
        await using var connection = await OpenConnectionAsync(cancellationToken);

        var migrationsTableExisted = await TableExistsAsync(
            connection,
            "SchemaMigrations",
            cancellationToken);
        HashSet<int> appliedVersions;
        if (migrationsTableExisted)
        {
            appliedVersions = await GetAppliedMigrationVersionsAsync(connection, cancellationToken);
        }
        else
        {
            appliedVersions = [];
        }

        var newestAppliedVersion = appliedVersions.DefaultIfEmpty(0).Max();
        if (newestAppliedVersion > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"This database uses schema version {newestAppliedVersion}, but this application supports only version {CurrentSchemaVersion}. " +
                "Install a compatible newer application build instead of opening the database with an older version.");
        }

        var pendingVersions = Enumerable.Range(1, CurrentSchemaVersion)
            .Where(version => !appliedVersions.Contains(version))
            .ToArray();

        if (pendingVersions.Length > 0 && databaseExistedBeforeStartup && createAutomaticBackup)
        {
            // Create the safety copy before adding the migrations table or changing
            // any existing application table.
            await _backupService.CreateBackupAsync(
                "pre-migration",
                newestAppliedVersion,
                includeSettings: true,
                cancellationToken);
        }

        await EnsureMigrationsTableAsync(connection, cancellationToken);
        foreach (var version in pendingVersions)
        {
            await ApplyMigrationAsync(connection, version, cancellationToken);
            await RecordMigrationAsync(connection, version, cancellationToken);
            _logger.Information($"Applied database migration {version} of {CurrentSchemaVersion}.");
        }

        await EnsureActiveSerialIndexAsync(connection, cancellationToken);
        var integrity = await VerifyIntegrityAsync(connection, cancellationToken);
        if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"SQLite integrity checking failed: {integrity}");
        }

        _logger.Information($"SQLite database initialized at schema version {CurrentSchemaVersion}.");
    }

    public async Task<int> GetCurrentSchemaVersionAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureMigrationsTableAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaMigrations;";
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
    }

    public async Task<string> VerifyIntegrityAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await VerifyIntegrityAsync(connection, cancellationToken);
    }

    public async Task RecordFileArtifactAsync(
        string transactionId,
        string artifactType,
        string path,
        string sha256,
        long sizeBytes,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR IGNORE INTO FileArtifacts(TransactionId,ArtifactType,Path,Sha256,SizeBytes,CreatedAt)
            VALUES($transactionId,$artifactType,$path,$sha256,$sizeBytes,$createdAt);
            """;
        command.Parameters.AddWithValue("$transactionId", transactionId);
        command.Parameters.AddWithValue("$artifactType", artifactType);
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$sha256", sha256);
        command.Parameters.AddWithValue("$sizeBytes", sizeBytes);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.Now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FileArtifactRecord>> GetFileArtifactsAsync(
        string? transactionId = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = string.IsNullOrWhiteSpace(transactionId)
            ? "SELECT Id,TransactionId,ArtifactType,Path,Sha256,SizeBytes,CreatedAt FROM FileArtifacts ORDER BY CreatedAt DESC;"
            : "SELECT Id,TransactionId,ArtifactType,Path,Sha256,SizeBytes,CreatedAt FROM FileArtifacts WHERE TransactionId=$transactionId ORDER BY CreatedAt DESC;";
        if (!string.IsNullOrWhiteSpace(transactionId))
        {
            command.Parameters.AddWithValue("$transactionId", transactionId.Trim());
        }

        var results = new List<FileArtifactRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new FileArtifactRecord
            {
                Id = reader.GetInt64(0),
                TransactionId = reader.GetString(1),
                ArtifactType = reader.GetString(2),
                Path = reader.GetString(3),
                Sha256 = reader.GetString(4),
                SizeBytes = reader.GetInt64(5),
                CreatedAt = ParseDate(reader.GetString(6))
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<PickupReceipt>> GetPickupReceiptsAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            return [];
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var receipts = new List<PickupReceipt>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = PickupReceiptSelectSql +
                " WHERE p.TransactionId=$transactionId ORDER BY p.SequenceNumber;";
            command.Parameters.AddWithValue("$transactionId", transactionId.Trim());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                receipts.Add(ReadPickupReceipt(reader));
            }
        }

        for (var index = 0; index < receipts.Count; index++)
        {
            receipts[index] = await WithPickupDeviceIdsAsync(
                connection,
                transaction: null,
                receipts[index],
                cancellationToken);
        }

        return receipts;
    }

    public async Task<PickupReceipt?> GetPickupReceiptByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await GetPickupReceiptByIdAsync(
            connection,
            transaction: null,
            id.Trim(),
            cancellationToken);
    }

    public async Task<int> GetNextPickupSequenceAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            throw new ArgumentException(
                "A parent 1297 transaction is required.",
                nameof(transactionId));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureActiveTransactionAllowsPickupAsync(
            connection,
            transaction: null,
            transactionId.Trim(),
            cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COALESCE(MAX(SequenceNumber),0)+1 FROM PartialPickups WHERE TransactionId=$transactionId;";
        command.Parameters.AddWithValue("$transactionId", transactionId.Trim());
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
    }

    public async Task<PickupReceiptCommitResult> CommitPickupReceiptAsync(
        PickupReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var normalized = NormalizeAndValidatePickupReceipt(receipt);
        await VerifyPickupReceiptFileAsync(normalized, cancellationToken);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        try
        {
            var existing = await GetPickupReceiptByIdAsync(
                connection,
                transaction,
                normalized.Id,
                cancellationToken);
            if (existing is not null)
            {
                if (!PickupReceiptsMatch(existing, normalized))
                {
                    throw new InvalidOperationException(
                        $"Pickup receipt '{normalized.Id}' was already committed with different data.");
                }

                var existingState = await GetPickupCommitStateAsync(
                    connection,
                    transaction,
                    existing,
                    cancellationToken);
                transaction.Commit();
                return new PickupReceiptCommitResult
                {
                    Receipt = existing,
                    PickedUpCount = existing.DeviceIds.Count,
                    RemainingDeviceCount = existingState.RemainingDeviceCount,
                    ParentArchived = existingState.ParentArchivedByReceipt,
                    WasAlreadyCommitted = true
                };
            }

            await EnsureActiveTransactionAllowsPickupAsync(
                connection,
                transaction,
                normalized.TransactionId,
                cancellationToken);

            var devices = await GetOrderedPickupDeviceStatesAsync(
                connection,
                transaction,
                normalized.TransactionId,
                cancellationToken);
            var requestedIds = normalized.DeviceIds.ToHashSet();
            var matched = devices
                .Where(item => requestedIds.Contains(item.DeviceId))
                .ToArray();
            if (matched.Length != requestedIds.Count)
            {
                throw new InvalidOperationException(
                    "Every selected device must belong to the active parent 1297.");
            }

            var alreadyReturned = matched
                .Where(item => string.Equals(
                    item.Status,
                    DeviceStatusCatalog.Returned,
                    StringComparison.OrdinalIgnoreCase))
                .Select(item => item.DeviceId)
                .ToArray();
            if (alreadyReturned.Length > 0)
            {
                throw new InvalidOperationException(
                    "A selected device was already returned and cannot be added to another pickup receipt.");
            }

            await using (var sequenceCommand = connection.CreateCommand())
            {
                sequenceCommand.Transaction = transaction;
                sequenceCommand.CommandText =
                    "SELECT COALESCE(MAX(SequenceNumber),0)+1 FROM PartialPickups WHERE TransactionId=$transactionId;";
                sequenceCommand.Parameters.AddWithValue(
                    "$transactionId",
                    normalized.TransactionId);
                var expectedSequence = Convert.ToInt32(
                    await sequenceCommand.ExecuteScalarAsync(cancellationToken),
                    CultureInfo.InvariantCulture);
                if (normalized.SequenceNumber != expectedSequence)
                {
                    throw new InvalidOperationException(
                        $"Pickup receipt sequence {normalized.SequenceNumber} is stale; expected {expectedSequence}.");
                }
            }

            long artifactId;
            await using (var artifactCommand = connection.CreateCommand())
            {
                artifactCommand.Transaction = transaction;
                artifactCommand.CommandText =
                    """
                    INSERT OR IGNORE INTO FileArtifacts(
                        TransactionId,ArtifactType,Path,Sha256,SizeBytes,CreatedAt)
                    VALUES($transactionId,$artifactType,$path,$sha256,$sizeBytes,$createdAt);
                    SELECT Id FROM FileArtifacts
                    WHERE TransactionId=$transactionId AND ArtifactType=$artifactType
                      AND Path=$path AND Sha256=$sha256
                    LIMIT 1;
                    """;
                artifactCommand.Parameters.AddWithValue(
                    "$transactionId",
                    normalized.TransactionId);
                artifactCommand.Parameters.AddWithValue(
                    "$artifactType",
                    SignedPartialPickupArtifactType);
                artifactCommand.Parameters.AddWithValue("$path", normalized.PdfPath);
                artifactCommand.Parameters.AddWithValue("$sha256", normalized.Sha256);
                artifactCommand.Parameters.AddWithValue("$sizeBytes", normalized.SizeBytes);
                artifactCommand.Parameters.AddWithValue(
                    "$createdAt",
                    normalized.CreatedAt.ToString("O"));
                var artifactValue = await artifactCommand.ExecuteScalarAsync(cancellationToken);
                if (artifactValue is null || artifactValue is DBNull)
                {
                    throw new InvalidOperationException(
                        "The signed pickup PDF artifact could not be recorded.");
                }
                artifactId = Convert.ToInt64(
                    artifactValue,
                    CultureInfo.InvariantCulture);
            }

            await using (var receiptCommand = connection.CreateCommand())
            {
                receiptCommand.Transaction = transaction;
                receiptCommand.CommandText =
                    """
                    INSERT INTO PartialPickups(
                        Id,TransactionId,ArtifactId,SequenceNumber,Technician,Notes,
                        PickedUpAt,SignerName,CertificateSubject,CertificateThumbprint,
                        SignatureTime,CreatedAt)
                    VALUES(
                        $id,$transactionId,$artifactId,$sequenceNumber,$technician,$notes,
                        $pickedUpAt,$signerName,$certificateSubject,$certificateThumbprint,
                        $signatureTime,$createdAt);
                    """;
                receiptCommand.Parameters.AddWithValue("$id", normalized.Id);
                receiptCommand.Parameters.AddWithValue(
                    "$transactionId",
                    normalized.TransactionId);
                receiptCommand.Parameters.AddWithValue("$artifactId", artifactId);
                receiptCommand.Parameters.AddWithValue(
                    "$sequenceNumber",
                    normalized.SequenceNumber);
                receiptCommand.Parameters.AddWithValue("$technician", normalized.Technician);
                receiptCommand.Parameters.AddWithValue("$notes", normalized.Notes);
                receiptCommand.Parameters.AddWithValue(
                    "$pickedUpAt",
                    normalized.PickedUpAt.ToString("O"));
                receiptCommand.Parameters.AddWithValue("$signerName", normalized.SignerName);
                receiptCommand.Parameters.AddWithValue(
                    "$certificateSubject",
                    normalized.CertificateSubject);
                receiptCommand.Parameters.AddWithValue(
                    "$certificateThumbprint",
                    normalized.CertificateThumbprint);
                receiptCommand.Parameters.AddWithValue(
                    "$signatureTime",
                    normalized.SignatureTime?.ToString("O") ?? (object)DBNull.Value);
                receiptCommand.Parameters.AddWithValue(
                    "$createdAt",
                    normalized.CreatedAt.ToString("O"));
                await receiptCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var device in matched.OrderBy(item => item.DeviceNumber))
            {
                await using (var linkCommand = connection.CreateCommand())
                {
                    linkCommand.Transaction = transaction;
                    linkCommand.CommandText =
                        """
                        INSERT INTO PartialPickupDevices(PartialPickupId,DeviceId,DeviceNumber)
                        VALUES($pickupId,$deviceId,$deviceNumber);
                        """;
                    linkCommand.Parameters.AddWithValue("$pickupId", normalized.Id);
                    linkCommand.Parameters.AddWithValue("$deviceId", device.DeviceId);
                    linkCommand.Parameters.AddWithValue("$deviceNumber", device.DeviceNumber);
                    await linkCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                await using (var updateCommand = connection.CreateCommand())
                {
                    updateCommand.Transaction = transaction;
                    updateCommand.CommandText =
                        """
                        UPDATE Devices
                        SET Status=$returned, ReturnedAt=$pickedUpAt, ReturnNotes=$notes
                        WHERE Id=$deviceId AND TransactionId=$transactionId
                          AND Status <> $returned;
                        """;
                    updateCommand.Parameters.AddWithValue(
                        "$returned",
                        DeviceStatusCatalog.Returned);
                    updateCommand.Parameters.AddWithValue(
                        "$pickedUpAt",
                        normalized.PickedUpAt.ToString("O"));
                    updateCommand.Parameters.AddWithValue("$notes", normalized.Notes);
                    updateCommand.Parameters.AddWithValue("$deviceId", device.DeviceId);
                    updateCommand.Parameters.AddWithValue(
                        "$transactionId",
                        normalized.TransactionId);
                    if (await updateCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
                    {
                        throw new InvalidOperationException(
                            $"Device {device.DeviceId} changed before the pickup receipt could be committed.");
                    }
                }

                await InsertAuditAsync(
                    connection,
                    transaction,
                    normalized.TransactionId,
                    device.DeviceId,
                    "Partial pickup completed",
                    $"{device.Status} -> {DeviceStatusCatalog.Returned}. " +
                    $"Signed pickup receipt {normalized.Id}, sequence {normalized.SequenceNumber}. " +
                    normalized.Notes,
                    normalized.Technician,
                    cancellationToken);
            }

            int remainingDeviceCount;
            await using (var remainingCommand = connection.CreateCommand())
            {
                remainingCommand.Transaction = transaction;
                remainingCommand.CommandText =
                    """
                    SELECT COUNT(*) FROM Devices
                    WHERE TransactionId=$transactionId AND Status <> $returned;
                    """;
                remainingCommand.Parameters.AddWithValue(
                    "$transactionId",
                    normalized.TransactionId);
                remainingCommand.Parameters.AddWithValue(
                    "$returned",
                    DeviceStatusCatalog.Returned);
                remainingDeviceCount = Convert.ToInt32(
                    await remainingCommand.ExecuteScalarAsync(cancellationToken),
                    CultureInfo.InvariantCulture);
            }

            var parentArchived = remainingDeviceCount == 0;
            if (parentArchived)
            {
                await using var archiveCommand = connection.CreateCommand();
                archiveCommand.Transaction = transaction;
                archiveCommand.CommandText =
                    """
                    UPDATE Transactions
                    SET IsArchived=1, ClosedAt=$closedAt,
                        CloseoutTechnician=$technician, PdfPath=$pdfPath
                    WHERE Id=$transactionId AND IsArchived=0
                      AND ClosedAt IS NULL AND CloseoutPreparedAt IS NULL;
                    """;
                archiveCommand.Parameters.AddWithValue(
                    "$closedAt",
                    normalized.PickedUpAt.ToString("O"));
                archiveCommand.Parameters.AddWithValue(
                    "$technician",
                    normalized.Technician);
                archiveCommand.Parameters.AddWithValue("$pdfPath", normalized.PdfPath);
                archiveCommand.Parameters.AddWithValue(
                    "$transactionId",
                    normalized.TransactionId);
                if (await archiveCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
                {
                    throw new InvalidOperationException(
                        "The parent 1297 changed before its final pickup receipt could be archived.");
                }
            }

            await InsertAuditAsync(
                connection,
                transaction,
                normalized.TransactionId,
                null,
                parentArchived
                    ? "Transaction closed by signed pickup receipt"
                    : "Signed partial pickup recorded",
                parentArchived
                    ? $"Receipt {normalized.Id} returned the final {matched.Length} device(s); the parent 1297 was archived."
                    : $"Receipt {normalized.Id} returned {matched.Length} device(s); {remainingDeviceCount} device(s) remain active.",
                normalized.Technician,
                cancellationToken);

            await UpsertTechnicianAsync(
                connection,
                transaction,
                normalized.Technician,
                normalized.PickedUpAt,
                cancellationToken);
            transaction.Commit();

            return new PickupReceiptCommitResult
            {
                Receipt = normalized,
                PickedUpCount = normalized.DeviceIds.Count,
                RemainingDeviceCount = remainingDeviceCount,
                ParentArchived = parentArchived,
                WasAlreadyCommitted = false
            };
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task ClearCloseoutPreparedAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            throw new ArgumentException(
                "A parent 1297 transaction is required.",
                nameof(transactionId));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Transactions SET CloseoutPreparedAt=NULL
            WHERE Id=$transactionId AND IsArchived=0 AND ClosedAt IS NULL;
            """;
        command.Parameters.AddWithValue("$transactionId", transactionId.Trim());
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException(
                "The selected active 1297 could not clear its closeout preparation state.");
        }
    }

    public async Task ResetOperationalDataAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                DELETE FROM PartialPickupDevices;
                DELETE FROM PartialPickups;
                DELETE FROM Audit;
                DELETE FROM Devices;
                DELETE FROM Transactions;
                DELETE FROM Technicians;
                DELETE FROM FileArtifacts;
                DELETE FROM WorkflowOperations;
                DELETE FROM sqlite_sequence WHERE name IN ('Devices','Audit','FileArtifacts');
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            transaction.Commit();
            _logger.Warning("All operational transaction, device, audit, technician, artifact, and workflow database records were reset by the current user.");
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<bool> InsertTransactionAsync(
        EquipmentTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        using var dbTransaction = connection.BeginTransaction();

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = dbTransaction;
            command.CommandText =
                """
                INSERT INTO Transactions (
                    Id, CustomerRank, CustomerFirstName, CustomerMiddleInitial,
                    CustomerLastName, CustomerOriginalName, PhoneNumber, Technician,
                    Organization, TicketNumber, IssuedAt, CreatedAt, PdfPath,
                    PdfSignerName, CertificateSubject, CertificateThumbprint,
                    SignatureTime, IsArchived, CloseoutPreparedAt, ClosedAt,
                    CloseoutTechnician)
                VALUES (
                    $id, $rank, $firstName, $middleInitial, $lastName, $originalName,
                    $phoneNumber, $technician, $organization, $ticketNumber, $issuedAt,
                    $createdAt, $pdfPath, $pdfSignerName, $certificateSubject,
                    $certificateThumbprint, $signatureTime, 0, NULL, NULL, '')
                ON CONFLICT(Id) DO NOTHING;
                """;
            AddTransactionParameters(command, transaction);

            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                dbTransaction.Rollback();
                _logger.Warning($"Transaction {transaction.Id} already exists. Duplicate insert skipped.");
                return false;
            }

            foreach (var device in transaction.Devices)
            {
                await using var deviceCommand = connection.CreateCommand();
                deviceCommand.Transaction = dbTransaction;
                deviceCommand.CommandText =
                    """
                    INSERT INTO Devices (
                        TransactionId, PartNumber, Model, SerialNumber, AssetTag, RawScanValue,
                        Status, ReturnedAt, ReturnCondition, ReturnNotes)
                    VALUES (
                        $transactionId, $partNumber, $model, $serialNumber, $assetTag, $rawScanValue,
                        $status, $returnedAt, $returnCondition, $returnNotes);
                    SELECT last_insert_rowid();
                    """;
                deviceCommand.Parameters.AddWithValue("$transactionId", transaction.Id);
                deviceCommand.Parameters.AddWithValue(
                    "$partNumber",
                    string.IsNullOrWhiteSpace(device.PartNumber) ? device.Model : device.PartNumber);
                deviceCommand.Parameters.AddWithValue("$model", device.Model);
                deviceCommand.Parameters.AddWithValue("$serialNumber", device.SerialNumber);
                deviceCommand.Parameters.AddWithValue("$assetTag", device.AssetTag);
                deviceCommand.Parameters.AddWithValue("$rawScanValue", device.RawScanValue);
                deviceCommand.Parameters.AddWithValue("$status", device.Status);
                deviceCommand.Parameters.AddWithValue("$returnedAt", device.ReturnedAt?.ToString("O") ?? (object)DBNull.Value);
                deviceCommand.Parameters.AddWithValue("$returnCondition", device.ReturnCondition);
                deviceCommand.Parameters.AddWithValue("$returnNotes", device.ReturnNotes);
                device.Id = Convert.ToInt64(
                    await deviceCommand.ExecuteScalarAsync(cancellationToken),
                    CultureInfo.InvariantCulture);
            }

            await UpsertTechnicianAsync(connection, dbTransaction, transaction.Technician, transaction.IssuedAt, cancellationToken);
            await InsertAuditAsync(
                connection,
                dbTransaction,
                transaction.Id,
                null,
                "Transaction finalized",
                $"Issued {transaction.Devices.Count} device(s).",
                transaction.Technician,
                cancellationToken);
            dbTransaction.Commit();
            return true;
        }
        catch
        {
            dbTransaction.Rollback();
            throw;
        }
    }

    public async Task<EquipmentTransaction?> GetTransactionByIdAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            return null;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        EquipmentTransaction? transaction = null;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = TransactionSelectSql + " WHERE Id = $id LIMIT 1;";
            command.Parameters.AddWithValue("$id", transactionId.Trim());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                transaction = ReadTransaction(reader);
            }
        }

        if (transaction is not null)
        {
            transaction.Devices = await GetDevicesForTransactionAsync(connection, transaction.Id, cancellationToken);
        }

        return transaction;
    }

    public async Task<IReadOnlyList<DeviceSearchResult>> SearchDevicesAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        var normalized = query?.Trim() ?? string.Empty;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT d.Id, d.TransactionId, t.TicketNumber, t.CustomerRank,
                   t.CustomerFirstName, t.CustomerMiddleInitial, t.CustomerLastName,
                   t.PhoneNumber, d.PartNumber, d.Model, d.SerialNumber, d.AssetTag, d.Status,
                   t.IssuedAt, t.PdfPath
            FROM Devices d
            INNER JOIN Transactions t ON t.Id = d.TransactionId
            WHERE t.IsArchived = 0
              AND ($query = '' OR
                   t.Id LIKE $like ESCAPE '\' OR t.TicketNumber LIKE $like ESCAPE '\' OR t.CustomerRank LIKE $like ESCAPE '\' OR
                   t.CustomerFirstName LIKE $like ESCAPE '\' OR t.CustomerMiddleInitial LIKE $like ESCAPE '\' OR
                   t.CustomerLastName LIKE $like ESCAPE '\' OR t.Organization LIKE $like ESCAPE '\' OR
                   t.PhoneNumber LIKE $like ESCAPE '\' OR d.PartNumber LIKE $like ESCAPE '\' OR d.Model LIKE $like ESCAPE '\' OR
                   d.SerialNumber LIKE $like ESCAPE '\' OR d.AssetTag LIKE $like ESCAPE '\' OR
                   d.Status LIKE $like ESCAPE '\')
            ORDER BY t.IssuedAt DESC, d.Id;
            """;
        command.Parameters.AddWithValue("$query", normalized);
        command.Parameters.AddWithValue("$like", $"%{EscapeLike(normalized)}%");

        var results = new List<DeviceSearchResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var identity = new CustomerIdentity
            {
                Rank = reader.GetString(3),
                FirstName = reader.GetString(4),
                MiddleInitial = reader.GetString(5),
                LastName = reader.GetString(6)
            };
            results.Add(new DeviceSearchResult
            {
                DeviceId = reader.GetInt64(0),
                TransactionId = reader.GetString(1),
                TicketNumber = reader.GetString(2),
                CustomerName = identity.DisplayName,
                PhoneNumber = reader.GetString(7),
                PartNumber = reader.GetString(8),
                Model = reader.GetString(9),
                SerialNumber = reader.GetString(10),
                AssetTag = reader.GetString(11),
                Status = reader.GetString(12),
                IssuedAt = ParseDate(reader.GetString(13)),
                PdfPath = reader.GetString(14)
            });
        }

        return results;
    }

    public async Task<DashboardSummary> GetDashboardSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                (SELECT COUNT(*) FROM Devices d JOIN Transactions t ON t.Id=d.TransactionId
                 WHERE t.IsArchived=0 AND d.Status <> 'Returned'),
                (SELECT COUNT(*) FROM Devices WHERE Status='Returned' AND date(ReturnedAt)=date('now','localtime')),
                (SELECT COUNT(*) FROM Transactions WHERE IsArchived=0),
                (SELECT COUNT(*) FROM Transactions WHERE IsArchived=1);
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new DashboardSummary
        {
            ActiveDeviceCount = reader.GetInt32(0),
            ReturnedTodayCount = reader.GetInt32(1),
            ActiveTransactionCount = reader.GetInt32(2),
            ArchivedTransactionCount = reader.GetInt32(3)
        };
    }

    public Task<IReadOnlyList<RecentTransactionItem>> GetRecentTransactionsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default) =>
        SearchTransactionsAsync(string.Empty, false, limit, cancellationToken);

    public async Task<IReadOnlyList<RecentTransactionItem>> SearchTransactionsAsync(
        string? query,
        bool archivedOnly = false,
        int limit = 250,
        CancellationToken cancellationToken = default)
    {
        var normalized = query?.Trim() ?? string.Empty;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT t.Id, t.TicketNumber, t.CustomerRank, t.CustomerFirstName,
                   t.CustomerMiddleInitial, t.CustomerLastName, t.Organization,
                   t.Technician, t.IssuedAt, t.ClosedAt, t.IsArchived, t.PdfPath,
                   COUNT(d.Id) AS DeviceCount,
                   COALESCE(GROUP_CONCAT(DISTINCT d.Status), '') AS StatusSummary
            FROM Transactions t
            LEFT JOIN Devices d ON d.TransactionId = t.Id
            WHERE t.IsArchived = $archived
              AND ($query = '' OR
                   t.Id LIKE $like ESCAPE '\' OR t.TicketNumber LIKE $like ESCAPE '\' OR
                   t.CustomerRank LIKE $like ESCAPE '\' OR t.CustomerFirstName LIKE $like ESCAPE '\' OR
                   t.CustomerMiddleInitial LIKE $like ESCAPE '\' OR t.CustomerLastName LIKE $like ESCAPE '\' OR
                   t.Organization LIKE $like ESCAPE '\' OR t.Technician LIKE $like ESCAPE '\' OR
                   EXISTS (SELECT 1 FROM Devices sd WHERE sd.TransactionId=t.Id AND
                       (sd.PartNumber LIKE $like ESCAPE '\' OR sd.Model LIKE $like ESCAPE '\' OR sd.SerialNumber LIKE $like ESCAPE '\' OR
                        sd.AssetTag LIKE $like ESCAPE '\' OR sd.Status LIKE $like ESCAPE '\')))
            GROUP BY t.Id
            ORDER BY COALESCE(t.ClosedAt, t.IssuedAt) DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$archived", archivedOnly ? 1 : 0);
        command.Parameters.AddWithValue("$query", normalized);
        command.Parameters.AddWithValue("$like", $"%{EscapeLike(normalized)}%");
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));

        var results = new List<RecentTransactionItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var identity = new CustomerIdentity
            {
                Rank = reader.GetString(2),
                FirstName = reader.GetString(3),
                MiddleInitial = reader.GetString(4),
                LastName = reader.GetString(5)
            };
            results.Add(new RecentTransactionItem
            {
                TransactionId = reader.GetString(0),
                TicketNumber = reader.GetString(1),
                CustomerName = identity.DisplayName,
                Organization = reader.GetString(6),
                Technician = reader.GetString(7),
                IssuedAt = ParseDate(reader.GetString(8)),
                ClosedAt = reader.IsDBNull(9) ? null : ParseDate(reader.GetString(9)),
                IsArchived = reader.GetInt32(10) != 0,
                PdfPath = reader.GetString(11),
                DeviceCount = reader.GetInt32(12),
                StatusSummary = reader.GetString(13)
            });
        }

        return results;
    }

    public async Task<bool> IsActiveSerialNumberAsync(
        string? serialNumber,
        long? excludedDeviceId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            return false;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS(
                SELECT 1 FROM Devices d
                JOIN Transactions t ON t.Id=d.TransactionId
                WHERE t.IsArchived=0 AND d.Status <> 'Returned'
                  AND TRIM(d.SerialNumber) = $serial COLLATE NOCASE
                  AND ($excludedId IS NULL OR d.Id <> $excludedId));
            """;
        command.Parameters.AddWithValue("$serial", serialNumber.Trim());
        command.Parameters.AddWithValue("$excludedId", (object?)excludedDeviceId ?? DBNull.Value);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) != 0;
    }

    public async Task<IReadOnlyList<TechnicianSuggestion>> GetTechnicianSuggestionsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT NameGrade, LastUsedAt, UseCount FROM Technicians ORDER BY LastUsedAt DESC, NameGrade LIMIT 100;";
        var results = new List<TechnicianSuggestion>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new TechnicianSuggestion
            {
                NameGrade = reader.GetString(0),
                LastUsedAt = ParseDate(reader.GetString(1)),
                UseCount = reader.GetInt32(2)
            });
        }
        return results;
    }

    public async Task<string?> ResolveModelNameAsync(
        string? partNumber,
        CancellationToken cancellationToken = default)
    {
        var normalized = partNumber?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return null;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT ModelName FROM ModelCatalog WHERE PartNumber=$partNumber COLLATE NOCASE LIMIT 1;";
        command.Parameters.AddWithValue("$partNumber", normalized);
        var value = Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture)?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public async Task<IReadOnlyList<DeviceIdentityHistoryItem>> GetDeviceIdentityHistoryAsync(
        string? serialNumber,
        CancellationToken cancellationToken = default)
    {
        var normalized = serialNumber?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return [];
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT PartNumber,Model,RawScanValue
            FROM Devices
            WHERE TRIM(SerialNumber) = $serial COLLATE NOCASE
            ORDER BY Id DESC;
            """;
        command.Parameters.AddWithValue("$serial", normalized);

        var results = new List<DeviceIdentityHistoryItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new DeviceIdentityHistoryItem
            {
                PartNumber = reader.GetString(0),
                ModelName = reader.GetString(1),
                RawScanValue = reader.GetString(2)
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<ModelCatalogEntry>> GetModelCatalogAsync(
        string? query = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = query?.Trim() ?? string.Empty;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT PartNumber,ModelName,CreatedAt,UpdatedAt,LastUsedAt,UseCount
            FROM ModelCatalog
            WHERE $query='' OR PartNumber LIKE $like ESCAPE '\' OR ModelName LIKE $like ESCAPE '\'
            ORDER BY LastUsedAt DESC, PartNumber
            LIMIT 500;
            """;
        command.Parameters.AddWithValue("$query", normalized);
        command.Parameters.AddWithValue("$like", $"%{EscapeLike(normalized)}%");
        var results = new List<ModelCatalogEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ModelCatalogEntry
            {
                PartNumber = reader.GetString(0),
                ModelName = reader.GetString(1),
                CreatedAt = ParseDate(reader.GetString(2)),
                UpdatedAt = ParseDate(reader.GetString(3)),
                LastUsedAt = ParseDate(reader.GetString(4)),
                UseCount = reader.GetInt32(5)
            });
        }

        return results;
    }

    public async Task UpsertModelCatalogEntryAsync(
        string partNumber,
        string modelName,
        string technician = "",
        string transactionId = "",
        CancellationToken cancellationToken = default)
    {
        var part = partNumber?.Trim() ?? string.Empty;
        var model = modelName?.Trim() ?? string.Empty;
        if (part.Length == 0 || model.Length == 0)
        {
            throw new ArgumentException("A part number and common model name are required.");
        }

        if (part.Length > 128 || model.Length > 128)
        {
            throw new ArgumentException("Part numbers and common model names are limited to 128 characters.");
        }

        var now = DateTimeOffset.Now.ToString("O");
        await using var connection = await OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO ModelCatalog(PartNumber,ModelName,CreatedAt,UpdatedAt,LastUsedAt,UseCount)
                    VALUES($partNumber,$modelName,$now,$now,$now,1)
                    ON CONFLICT(PartNumber) DO UPDATE SET
                        ModelName=excluded.ModelName,
                        UpdatedAt=excluded.UpdatedAt,
                        LastUsedAt=excluded.LastUsedAt,
                        UseCount=ModelCatalog.UseCount+1;

                    UPDATE Devices SET Model=$modelName
                    WHERE PartNumber=$partNumber COLLATE NOCASE;
                    """;
                command.Parameters.AddWithValue("$partNumber", part);
                command.Parameters.AddWithValue("$modelName", model);
                command.Parameters.AddWithValue("$now", now);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(transactionId))
            {
                await InsertAuditAsync(
                    connection,
                    transaction,
                    transactionId.Trim(),
                    null,
                    "Model catalog updated",
                    $"Mapped part number '{part}' to common model name '{model}'.",
                    technician,
                    cancellationToken);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<IReadOnlyList<TransactionDeviceStatusItem>> GetTransactionDeviceStatusItemsAsync(
        string transactionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, TransactionId, Model, PartNumber, AssetTag, SerialNumber, Status
            FROM Devices WHERE TransactionId=$transactionId ORDER BY Id;
            """;
        command.Parameters.AddWithValue("$transactionId", transactionId);
        var results = new List<TransactionDeviceStatusItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var number = 1;
        while (await reader.ReadAsync(cancellationToken))
        {
            var status = reader.GetString(6);
            results.Add(new TransactionDeviceStatusItem
            {
                DeviceId = reader.GetInt64(0),
                TransactionId = reader.GetString(1),
                DeviceNumber = number++,
                Model = reader.GetString(2),
                PartNumber = reader.GetString(3),
                AssetTag = reader.GetString(4),
                SerialNumber = reader.GetString(5),
                OriginalStatus = status,
                Status = status,
                IsPickedUp = string.Equals(status, DeviceStatusCatalog.Returned, StringComparison.OrdinalIgnoreCase)
            });
        }
        return results;
    }

    public async Task UpdateDeviceStatusAsync(
        long deviceId,
        string status,
        string technician,
        string notes,
        CancellationToken cancellationToken = default)
    {
        await UpdateDeviceStatusesAsync(
            [new DeviceStatusUpdate
            {
                DeviceId = deviceId,
                Status = status,
                MarkReturned = string.Equals(status, DeviceStatusCatalog.Returned, StringComparison.OrdinalIgnoreCase)
            }],
            technician,
            notes,
            cancellationToken);
    }

    public async Task UpdateDeviceStatusesAsync(
        IReadOnlyList<DeviceStatusUpdate> updates,
        string technician,
        string notes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count == 0)
        {
            return;
        }
        if (updates.Select(update => update.DeviceId).Distinct().Count() != updates.Count)
        {
            throw new InvalidOperationException(
                "A device cannot appear more than once in the same status update.");
        }
        if (string.IsNullOrWhiteSpace(technician) || technician.Length > 120)
        {
            throw new InvalidOperationException("A valid technician name and grade is required.");
        }
        if ((notes?.Length ?? 0) > 1000)
        {
            throw new InvalidOperationException("Status notes cannot exceed 1000 characters.");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        try
        {
            foreach (var update in updates)
            {
                var normalizedStatus = update.MarkReturned
                    ? DeviceStatusCatalog.Returned
                    : update.Status?.Trim() ?? string.Empty;
                if (!DeviceStatusCatalog.IsValidStatus(normalizedStatus))
                {
                    throw new InvalidOperationException($"Unsupported device status '{update.Status}'.");
                }
                if (string.Equals(
                    normalizedStatus,
                    DeviceStatusCatalog.Returned,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Returned equipment must be completed through a verified signed pickup or final closeout workflow.");
                }

                await using var lookup = connection.CreateCommand();
                lookup.Transaction = transaction;
                lookup.CommandText =
                    """
                    SELECT d.TransactionId, d.Status, t.IsArchived,
                           t.CloseoutPreparedAt, t.ClosedAt
                    FROM Devices d
                    INNER JOIN Transactions t ON t.Id=d.TransactionId
                    WHERE d.Id=$id;
                    """;
                lookup.Parameters.AddWithValue("$id", update.DeviceId);
                string transactionId;
                string oldStatus;
                await using (var reader = await lookup.ExecuteReaderAsync(cancellationToken))
                {
                    if (!await reader.ReadAsync(cancellationToken))
                    {
                        throw new InvalidOperationException($"Device {update.DeviceId} was not found.");
                    }
                    transactionId = reader.GetString(0);
                    oldStatus = reader.GetString(1);
                    if (reader.GetInt32(2) != 0 || !reader.IsDBNull(4))
                    {
                        throw new InvalidOperationException(
                            "Devices on an archived 1297 cannot be changed.");
                    }
                    if (!reader.IsDBNull(3))
                    {
                        throw new InvalidOperationException(
                            "Device status cannot change while final closeout is in progress.");
                    }
                    if (string.Equals(
                        oldStatus,
                        DeviceStatusCatalog.Returned,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "A returned device cannot be reactivated or changed.");
                    }
                }

                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    UPDATE Devices SET Status=$status,
                        ReturnedAt=NULL,
                        ReturnNotes=$notes
                    WHERE Id=$id AND Status=$oldStatus
                      AND EXISTS (
                          SELECT 1 FROM Transactions t
                          WHERE t.Id=Devices.TransactionId
                            AND t.IsArchived=0 AND t.ClosedAt IS NULL
                            AND t.CloseoutPreparedAt IS NULL);
                    """;
                command.Parameters.AddWithValue("$status", normalizedStatus);
                command.Parameters.AddWithValue("$notes", notes?.Trim() ?? string.Empty);
                command.Parameters.AddWithValue("$id", update.DeviceId);
                command.Parameters.AddWithValue("$oldStatus", oldStatus);
                if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                {
                    throw new InvalidOperationException(
                        $"Device {update.DeviceId} changed before its status update could be committed.");
                }

                await InsertAuditAsync(
                    connection,
                    transaction,
                    transactionId,
                    update.DeviceId,
                    "Device status changed",
                    $"{oldStatus} -> {normalizedStatus}. {notes}".Trim(),
                    technician,
                    cancellationToken);
            }

            await UpsertTechnicianAsync(connection, transaction, technician, DateTimeOffset.Now, cancellationToken);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task MarkCloseoutPreparedAsync(
        string transactionId,
        string technician,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(technician) || technician.Length > 120)
        {
            throw new InvalidOperationException("A valid technician name and grade is required.");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE Transactions SET CloseoutPreparedAt=$prepared
            WHERE Id=$id AND IsArchived=0;
            """;
        command.Parameters.AddWithValue("$prepared", DateTimeOffset.Now.ToString("O"));
        command.Parameters.AddWithValue("$id", transactionId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            transaction.Rollback();
            throw new InvalidOperationException("The selected active 1297 could not be prepared for closeout.");
        }

        await InsertAuditAsync(connection, transaction, transactionId, null, "Closeout started", "Pickup signature requested.", technician, cancellationToken);
        transaction.Commit();
    }

    public async Task ArchiveTransactionAsync(
        string transactionId,
        string archivedPdfPath,
        string technician,
        DateTimeOffset closedAt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(technician) || technician.Length > 120)
        {
            throw new InvalidOperationException("A valid closeout technician name and grade is required.");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE Transactions
                SET IsArchived=1, ClosedAt=$closedAt, CloseoutTechnician=$technician,
                    PdfPath=$pdfPath
                WHERE Id=$id AND IsArchived=0;
                """;
            command.Parameters.AddWithValue("$closedAt", closedAt.ToString("O"));
            command.Parameters.AddWithValue("$technician", technician.Trim());
            command.Parameters.AddWithValue("$pdfPath", archivedPdfPath);
            command.Parameters.AddWithValue("$id", transactionId);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                throw new InvalidOperationException("The selected 1297 was already archived or no longer exists.");
            }

            await using var deviceCommand = connection.CreateCommand();
            deviceCommand.Transaction = transaction;
            deviceCommand.CommandText =
                """
                UPDATE Devices SET Status='Returned', ReturnedAt=$closedAt
                WHERE TransactionId=$id AND Status <> 'Returned';
                """;
            deviceCommand.Parameters.AddWithValue("$closedAt", closedAt.ToString("O"));
            deviceCommand.Parameters.AddWithValue("$id", transactionId);
            await deviceCommand.ExecuteNonQueryAsync(cancellationToken);

            await UpsertTechnicianAsync(connection, transaction, technician, closedAt, cancellationToken);
            await InsertAuditAsync(connection, transaction, transactionId, null, "Transaction closed and archived", "All devices marked returned.", technician, cancellationToken);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<IReadOnlyList<ChartDataPoint>> GetDeviceChartDataAsync(
        string metric,
        CancellationToken cancellationToken = default)
    {
        var expression = metric switch
        {
            "Model" => "d.Model",
            "Customer" => "TRIM(COALESCE(t.CustomerRank || ' ', '') || t.CustomerLastName || ', ' || t.CustomerFirstName || ' ' || t.CustomerMiddleInitial)",
            _ => "d.Status"
        };
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT CASE WHEN TRIM({expression})='' THEN 'Unspecified' ELSE TRIM({expression}) END, COUNT(*)
            FROM Devices d JOIN Transactions t ON t.Id=d.TransactionId
            WHERE t.IsArchived=0 AND d.Status <> 'Returned'
            GROUP BY 1 ORDER BY COUNT(*) DESC, 1 LIMIT 20;
            """;
        var results = new List<ChartDataPoint>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ChartDataPoint { Label = reader.GetString(0), Value = reader.GetInt32(1) });
        }
        return results;
    }

    public async Task<DatabaseSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var transactions = new Dictionary<string, EquipmentTransaction>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = TransactionSelectSql + " ORDER BY IssuedAt DESC;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var transaction = ReadTransaction(reader);
                transactions[transaction.Id] = transaction;
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT Id, TransactionId, Model, SerialNumber, AssetTag, RawScanValue,
                       Status, ReturnedAt, ReturnCondition, ReturnNotes, PartNumber
                FROM Devices ORDER BY Id;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (transactions.TryGetValue(reader.GetString(1), out var transaction))
                {
                    transaction.Devices.Add(ReadDevice(reader));
                }
            }
        }

        var audit = new List<AuditRecord>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT Id,TransactionId,DeviceId,Action,Details,Technician,ComputerName,ActionTime FROM Audit ORDER BY ActionTime DESC;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                audit.Add(new AuditRecord
                {
                    Id = reader.GetInt64(0),
                    TransactionId = reader.GetString(1),
                    DeviceId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
                    Action = reader.GetString(3),
                    Details = reader.GetString(4),
                    Technician = reader.GetString(5),
                    ComputerName = reader.GetString(6),
                    ActionTime = ParseDate(reader.GetString(7))
                });
            }
        }

        return new DatabaseSnapshot
        {
            Transactions = transactions.Values.OrderByDescending(item => item.IssuedAt).ToList(),
            AuditRecords = audit
        };
    }

    private const string PickupReceiptSelectSql =
        """
        SELECT p.Id, p.TransactionId, p.SequenceNumber, f.Path,
               p.Technician, p.Notes, p.PickedUpAt, f.Sha256, f.SizeBytes,
               p.SignerName, p.CertificateSubject, p.CertificateThumbprint,
               p.SignatureTime, p.CreatedAt
        FROM PartialPickups p
        INNER JOIN FileArtifacts f ON f.Id=p.ArtifactId
        """;

    private static PickupReceipt ReadPickupReceipt(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        TransactionId = reader.GetString(1),
        SequenceNumber = reader.GetInt32(2),
        PdfPath = reader.GetString(3),
        Technician = reader.GetString(4),
        Notes = reader.GetString(5),
        PickedUpAt = ParseDate(reader.GetString(6)),
        Sha256 = reader.GetString(7),
        SizeBytes = reader.GetInt64(8),
        SignerName = reader.GetString(9),
        CertificateSubject = reader.GetString(10),
        CertificateThumbprint = reader.GetString(11),
        SignatureTime = reader.IsDBNull(12) ? null : ParseDate(reader.GetString(12)),
        CreatedAt = ParseDate(reader.GetString(13))
    };

    private static async Task<PickupReceipt?> GetPickupReceiptByIdAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string id,
        CancellationToken cancellationToken)
    {
        PickupReceipt? receipt = null;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = PickupReceiptSelectSql + " WHERE p.Id=$id LIMIT 1;";
            command.Parameters.AddWithValue("$id", id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                receipt = ReadPickupReceipt(reader);
            }
        }

        return receipt is null
            ? null
            : await WithPickupDeviceIdsAsync(
                connection,
                transaction,
                receipt,
                cancellationToken);
    }

    private static async Task<PickupReceipt> WithPickupDeviceIdsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        PickupReceipt receipt,
        CancellationToken cancellationToken)
    {
        var deviceIds = new List<long>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT DeviceId FROM PartialPickupDevices
            WHERE PartialPickupId=$pickupId
            ORDER BY DeviceNumber;
            """;
        command.Parameters.AddWithValue("$pickupId", receipt.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            deviceIds.Add(reader.GetInt64(0));
        }

        return new PickupReceipt
        {
            Id = receipt.Id,
            TransactionId = receipt.TransactionId,
            SequenceNumber = receipt.SequenceNumber,
            PdfPath = receipt.PdfPath,
            Technician = receipt.Technician,
            Notes = receipt.Notes,
            PickedUpAt = receipt.PickedUpAt,
            Sha256 = receipt.Sha256,
            SizeBytes = receipt.SizeBytes,
            SignerName = receipt.SignerName,
            CertificateSubject = receipt.CertificateSubject,
            CertificateThumbprint = receipt.CertificateThumbprint,
            SignatureTime = receipt.SignatureTime,
            CreatedAt = receipt.CreatedAt,
            DeviceIds = deviceIds
        };
    }

    private static async Task EnsureActiveTransactionAllowsPickupAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string transactionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT IsArchived, CloseoutPreparedAt, ClosedAt
            FROM Transactions WHERE Id=$transactionId LIMIT 1;
            """;
        command.Parameters.AddWithValue("$transactionId", transactionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "The parent 1297 transaction could not be found.");
        }
        if (reader.GetInt32(0) != 0 || !reader.IsDBNull(2))
        {
            throw new InvalidOperationException(
                "The parent 1297 is already archived and cannot accept another pickup receipt.");
        }
        if (!reader.IsDBNull(1))
        {
            throw new InvalidOperationException(
                "The parent 1297 already has a closeout in progress.");
        }
    }

    private static async Task<IReadOnlyList<PickupDeviceState>> GetOrderedPickupDeviceStatesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string transactionId,
        CancellationToken cancellationToken)
    {
        var devices = new List<PickupDeviceState>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT Id,Status FROM Devices WHERE TransactionId=$transactionId ORDER BY Id;";
        command.Parameters.AddWithValue("$transactionId", transactionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var deviceNumber = 1;
        while (await reader.ReadAsync(cancellationToken))
        {
            devices.Add(new PickupDeviceState(
                reader.GetInt64(0),
                deviceNumber++,
                reader.GetString(1)));
        }
        return devices;
    }

    private static async Task<PickupCommitState> GetPickupCommitStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PickupReceipt receipt,
        CancellationToken cancellationToken)
    {
        int remainingDeviceCount;
        await using (var remainingCommand = connection.CreateCommand())
        {
            remainingCommand.Transaction = transaction;
            remainingCommand.CommandText =
                """
                SELECT COUNT(*) FROM Devices
                WHERE TransactionId=$transactionId AND Status <> $returned;
                """;
            remainingCommand.Parameters.AddWithValue(
                "$transactionId",
                receipt.TransactionId);
            remainingCommand.Parameters.AddWithValue(
                "$returned",
                DeviceStatusCatalog.Returned);
            remainingDeviceCount = Convert.ToInt32(
                await remainingCommand.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
        }

        var parentArchivedByReceipt = false;
        await using (var parentCommand = connection.CreateCommand())
        {
            parentCommand.Transaction = transaction;
            parentCommand.CommandText =
                "SELECT IsArchived,PdfPath FROM Transactions WHERE Id=$transactionId LIMIT 1;";
            parentCommand.Parameters.AddWithValue(
                "$transactionId",
                receipt.TransactionId);
            await using var reader = await parentCommand.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                parentArchivedByReceipt = reader.GetInt32(0) != 0 &&
                    PathsEqual(reader.GetString(1), receipt.PdfPath);
            }
        }

        return new PickupCommitState(
            remainingDeviceCount,
            parentArchivedByReceipt);
    }

    private static PickupReceipt NormalizeAndValidatePickupReceipt(PickupReceipt receipt)
    {
        var id = receipt.Id?.Trim() ?? string.Empty;
        var transactionId = receipt.TransactionId?.Trim() ?? string.Empty;
        var technician = receipt.Technician?.Trim() ?? string.Empty;
        var notes = receipt.Notes?.Trim() ?? string.Empty;
        var path = receipt.PdfPath?.Trim() ?? string.Empty;
        var sha256 = receipt.Sha256?.Trim().ToUpperInvariant() ?? string.Empty;
        var signerName = receipt.SignerName?.Trim() ?? string.Empty;
        var certificateSubject = receipt.CertificateSubject?.Trim() ?? string.Empty;
        var certificateThumbprint = receipt.CertificateThumbprint?.Trim() ?? string.Empty;
        var deviceIds = receipt.DeviceIds?.ToArray() ?? [];

        ValidatePickupText(id, 180, "Pickup receipt identifier", required: true);
        ValidatePickupText(transactionId, 180, "Parent 1297 identifier", required: true);
        ValidatePickupText(technician, 120, "Pickup technician", required: true);
        ValidatePickupText(notes, 1000, "Pickup notes", required: false);
        ValidatePickupText(signerName, 512, "Pickup signer name", required: false);
        ValidatePickupText(certificateSubject, 2048, "Pickup certificate subject", required: false);
        ValidatePickupText(certificateThumbprint, 256, "Pickup certificate thumbprint", required: false);
        if (receipt.SequenceNumber <= 0)
        {
            throw new InvalidOperationException(
                "The pickup receipt sequence number must be positive.");
        }
        if (deviceIds.Length == 0 || deviceIds.Any(deviceId => deviceId <= 0))
        {
            throw new InvalidOperationException(
                "At least one valid device must be selected for pickup.");
        }
        if (deviceIds.Distinct().Count() != deviceIds.Length)
        {
            throw new InvalidOperationException(
                "A device cannot appear more than once on the same pickup receipt.");
        }
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException(
                "The signed pickup PDF must use an absolute path.");
        }
        path = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The signed pickup artifact must be a PDF file.");
        }
        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException(
                "The signed pickup PDF must have a valid SHA-256 digest.");
        }
        if (receipt.SizeBytes <= 0)
        {
            throw new InvalidOperationException(
                "The signed pickup PDF size must be positive.");
        }

        return new PickupReceipt
        {
            Id = id,
            TransactionId = transactionId,
            SequenceNumber = receipt.SequenceNumber,
            PdfPath = path,
            Technician = technician,
            Notes = notes,
            PickedUpAt = receipt.PickedUpAt,
            Sha256 = sha256,
            SizeBytes = receipt.SizeBytes,
            SignerName = signerName,
            CertificateSubject = certificateSubject,
            CertificateThumbprint = certificateThumbprint,
            SignatureTime = receipt.SignatureTime,
            CreatedAt = receipt.CreatedAt,
            DeviceIds = deviceIds
        };
    }

    private static void ValidatePickupText(
        string value,
        int maximumLength,
        string label,
        bool required)
    {
        if (required && string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label} is required.");
        }
        if (value.Length > maximumLength)
        {
            throw new InvalidOperationException(
                $"{label} cannot exceed {maximumLength} characters.");
        }
    }

    private static async Task VerifyPickupReceiptFileAsync(
        PickupReceipt receipt,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(receipt.PdfPath))
        {
            throw new FileNotFoundException(
                "The signed pickup PDF was not found.",
                receipt.PdfPath);
        }

        var fileInfo = new FileInfo(receipt.PdfPath);
        if (fileInfo.Length != receipt.SizeBytes)
        {
            throw new InvalidDataException(
                "The signed pickup PDF size does not match its verified metadata.");
        }

        await using var stream = new FileStream(
            receipt.PdfPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actualHash = Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(
                actualHash,
                receipt.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The signed pickup PDF failed SHA-256 verification.");
        }
    }

    private static bool PickupReceiptsMatch(
        PickupReceipt existing,
        PickupReceipt requested)
    {
        return string.Equals(existing.TransactionId, requested.TransactionId, StringComparison.Ordinal) &&
               existing.SequenceNumber == requested.SequenceNumber &&
               PathsEqual(existing.PdfPath, requested.PdfPath) &&
               string.Equals(existing.Technician, requested.Technician, StringComparison.Ordinal) &&
               string.Equals(existing.Notes, requested.Notes, StringComparison.Ordinal) &&
               SameInstant(existing.PickedUpAt, requested.PickedUpAt) &&
               string.Equals(existing.Sha256, requested.Sha256, StringComparison.OrdinalIgnoreCase) &&
               existing.SizeBytes == requested.SizeBytes &&
               string.Equals(existing.SignerName, requested.SignerName, StringComparison.Ordinal) &&
               string.Equals(existing.CertificateSubject, requested.CertificateSubject, StringComparison.Ordinal) &&
               string.Equals(existing.CertificateThumbprint, requested.CertificateThumbprint, StringComparison.OrdinalIgnoreCase) &&
               SameNullableInstant(existing.SignatureTime, requested.SignatureTime) &&
               existing.DeviceIds.Order().SequenceEqual(requested.DeviceIds.Order());
    }

    private static bool PathsEqual(string first, string second)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(first),
                Path.GetFullPath(second),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool SameInstant(DateTimeOffset first, DateTimeOffset second) =>
        first.UtcTicks == second.UtcTicks;

    private static bool SameNullableInstant(
        DateTimeOffset? first,
        DateTimeOffset? second) =>
        first.HasValue == second.HasValue &&
        (!first.HasValue || SameInstant(first.Value, second!.Value));

    private sealed record PickupDeviceState(
        long DeviceId,
        int DeviceNumber,
        string Status);

    private sealed record PickupCommitState(
        int RemainingDeviceCount,
        bool ParentArchivedByReceipt);

    private const string TransactionSelectSql =
        """
        SELECT Id, CustomerRank, CustomerFirstName, CustomerMiddleInitial,
               CustomerLastName, CustomerOriginalName, PhoneNumber, Technician,
               Organization, TicketNumber, IssuedAt, CreatedAt, PdfPath,
               PdfSignerName, CertificateSubject, CertificateThumbprint,
               SignatureTime, IsArchived, CloseoutPreparedAt, ClosedAt,
               CloseoutTechnician
        FROM Transactions
        """;

    private static EquipmentTransaction ReadTransaction(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Customer = new CustomerIdentity
        {
            Rank = reader.GetString(1),
            FirstName = reader.GetString(2),
            MiddleInitial = reader.GetString(3),
            LastName = reader.GetString(4),
            OriginalName = reader.GetString(5)
        },
        PhoneNumber = reader.GetString(6),
        Technician = reader.GetString(7),
        Organization = reader.GetString(8),
        TicketNumber = reader.GetString(9),
        IssuedAt = ParseDate(reader.GetString(10)),
        CreatedAt = ParseDate(reader.GetString(11)),
        PdfPath = reader.GetString(12),
        PdfSignerName = reader.GetString(13),
        CertificateSubject = reader.GetString(14),
        CertificateThumbprint = reader.GetString(15),
        SignatureTime = reader.IsDBNull(16) ? null : ParseDate(reader.GetString(16)),
        IsArchived = reader.GetInt32(17) != 0,
        CloseoutPreparedAt = reader.IsDBNull(18) ? null : ParseDate(reader.GetString(18)),
        ClosedAt = reader.IsDBNull(19) ? null : ParseDate(reader.GetString(19)),
        CloseoutTechnician = reader.GetString(20)
    };

    private static DeviceRecord ReadDevice(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        PartNumber = reader.GetString(10),
        Model = reader.GetString(2),
        SerialNumber = reader.GetString(3),
        AssetTag = reader.GetString(4),
        RawScanValue = reader.GetString(5),
        Status = reader.GetString(6),
        ReturnedAt = reader.IsDBNull(7) ? null : ParseDate(reader.GetString(7)),
        ReturnCondition = reader.GetString(8),
        ReturnNotes = reader.GetString(9)
    };

    private static async Task<List<DeviceRecord>> GetDevicesForTransactionAsync(
        SqliteConnection connection,
        string transactionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, TransactionId, Model, SerialNumber, AssetTag, RawScanValue,
                   Status, ReturnedAt, ReturnCondition, ReturnNotes, PartNumber
            FROM Devices WHERE TransactionId=$id ORDER BY Id;
            """;
        command.Parameters.AddWithValue("$id", transactionId);
        var devices = new List<DeviceRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            devices.Add(ReadDevice(reader));
        }
        return devices;
    }

    private static void AddTransactionParameters(SqliteCommand command, EquipmentTransaction transaction)
    {
        command.Parameters.AddWithValue("$id", transaction.Id);
        command.Parameters.AddWithValue("$rank", transaction.Customer.Rank);
        command.Parameters.AddWithValue("$firstName", transaction.Customer.FirstName);
        command.Parameters.AddWithValue("$middleInitial", transaction.Customer.MiddleInitial);
        command.Parameters.AddWithValue("$lastName", transaction.Customer.LastName);
        command.Parameters.AddWithValue("$originalName", transaction.Customer.OriginalName);
        command.Parameters.AddWithValue("$phoneNumber", transaction.PhoneNumber);
        command.Parameters.AddWithValue("$technician", transaction.Technician);
        command.Parameters.AddWithValue("$organization", transaction.Organization);
        command.Parameters.AddWithValue("$ticketNumber", transaction.TicketNumber);
        command.Parameters.AddWithValue("$issuedAt", transaction.IssuedAt.ToString("O"));
        command.Parameters.AddWithValue("$createdAt", transaction.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$pdfPath", transaction.PdfPath);
        command.Parameters.AddWithValue("$pdfSignerName", transaction.PdfSignerName);
        command.Parameters.AddWithValue("$certificateSubject", transaction.CertificateSubject);
        command.Parameters.AddWithValue("$certificateThumbprint", transaction.CertificateThumbprint);
        command.Parameters.AddWithValue("$signatureTime", transaction.SignatureTime?.ToString("O") ?? (object)DBNull.Value);
    }


    private static async Task EnsureActiveSerialIndexAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var duplicateSerials = new List<string>();
        await using (var duplicateCheck = connection.CreateCommand())
        {
            duplicateCheck.CommandText =
                """
                SELECT MIN(TRIM(SerialNumber)) AS SerialNumber
                FROM Devices
                WHERE TRIM(SerialNumber) <> '' AND Status <> 'Returned'
                GROUP BY TRIM(SerialNumber) COLLATE NOCASE
                HAVING COUNT(*) > 1
                ORDER BY MIN(Id)
                LIMIT 10;
                """;
            await using var reader = await duplicateCheck.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                duplicateSerials.Add(reader.GetString(0));
            }
        }

        if (duplicateSerials.Count > 0)
        {
            throw new InvalidOperationException(
                "The database contains duplicate active serial numbers that differ only by " +
                $"capitalization or spacing: {string.Join(", ", duplicateSerials)}. " +
                "Resolve those records before starting the application.");
        }

        using var transaction = connection.BeginTransaction();
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                DROP INDEX IF EXISTS IX_Devices_ActiveSerial;
                CREATE UNIQUE INDEX IX_Devices_ActiveSerial
                    ON Devices(TRIM(SerialNumber) COLLATE NOCASE)
                    WHERE TRIM(SerialNumber) <> '' AND Status <> 'Returned';
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name);";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) == 1;
    }

    private static async Task EnsureMigrationsTableAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS SchemaMigrations (
                Version INTEGER PRIMARY KEY,
                AppliedAt TEXT NOT NULL,
                ApplicationVersion TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<HashSet<int>> GetAppliedMigrationVersionsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var versions = new HashSet<int>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Version FROM SchemaMigrations ORDER BY Version;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            versions.Add(reader.GetInt32(0));
        }
        return versions;
    }

    private static async Task RecordMigrationAsync(
        SqliteConnection connection,
        int version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO SchemaMigrations(Version,AppliedAt,ApplicationVersion)
            VALUES($version,$appliedAt,$applicationVersion)
            ON CONFLICT(Version) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$appliedAt", DateTimeOffset.Now.ToString("O"));
        command.Parameters.AddWithValue(
            "$applicationVersion",
            typeof(DatabaseService).Assembly.GetName().Version?.ToString(3) ?? "unknown");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ApplyMigrationAsync(
        SqliteConnection connection,
        int version,
        CancellationToken cancellationToken)
    {
        switch (version)
        {
            case 1:
                await ExecuteMigrationSqlAsync(connection,
                    """
                    PRAGMA journal_mode = WAL;
                    PRAGMA foreign_keys = ON;

                    CREATE TABLE IF NOT EXISTS Transactions (
                        Id TEXT PRIMARY KEY,
                        CustomerRank TEXT NOT NULL DEFAULT '',
                        CustomerFirstName TEXT NOT NULL,
                        CustomerMiddleInitial TEXT NOT NULL,
                        CustomerLastName TEXT NOT NULL,
                        CustomerOriginalName TEXT NOT NULL,
                        PhoneNumber TEXT NOT NULL,
                        Technician TEXT NOT NULL,
                        Organization TEXT NOT NULL DEFAULT '',
                        TicketNumber TEXT NOT NULL DEFAULT '',
                        IssuedAt TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        PdfPath TEXT NOT NULL,
                        PdfSignerName TEXT NOT NULL,
                        CertificateSubject TEXT NOT NULL,
                        CertificateThumbprint TEXT NOT NULL,
                        SignatureTime TEXT NULL,
                        IsArchived INTEGER NOT NULL DEFAULT 0,
                        CloseoutPreparedAt TEXT NULL,
                        ClosedAt TEXT NULL,
                        CloseoutTechnician TEXT NOT NULL DEFAULT ''
                    );

                    CREATE TABLE IF NOT EXISTS Devices (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TransactionId TEXT NOT NULL,
                        Model TEXT NOT NULL,
                        SerialNumber TEXT NOT NULL,
                        AssetTag TEXT NOT NULL,
                        RawScanValue TEXT NOT NULL,
                        Status TEXT NOT NULL,
                        ReturnedAt TEXT NULL,
                        ReturnCondition TEXT NOT NULL DEFAULT '',
                        ReturnNotes TEXT NOT NULL DEFAULT '',
                        FOREIGN KEY (TransactionId) REFERENCES Transactions(Id) ON DELETE CASCADE
                    );

                    CREATE TABLE IF NOT EXISTS Audit (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TransactionId TEXT NOT NULL,
                        DeviceId INTEGER NULL,
                        Action TEXT NOT NULL,
                        Details TEXT NOT NULL,
                        Technician TEXT NOT NULL,
                        ComputerName TEXT NOT NULL,
                        ActionTime TEXT NOT NULL
                    );

                    CREATE TABLE IF NOT EXISTS Technicians (
                        NameGrade TEXT PRIMARY KEY COLLATE NOCASE,
                        LastUsedAt TEXT NOT NULL,
                        UseCount INTEGER NOT NULL DEFAULT 1
                    );
                    """, cancellationToken);
                break;

            case 2:
                await EnsureTransactionColumnAsync(connection, "Organization", "TEXT NOT NULL DEFAULT ''", cancellationToken);
                await EnsureTransactionColumnAsync(connection, "TicketNumber", "TEXT NOT NULL DEFAULT ''", cancellationToken);
                await EnsureTransactionColumnAsync(connection, "CustomerRank", "TEXT NOT NULL DEFAULT ''", cancellationToken);
                await EnsureTransactionColumnAsync(connection, "IsArchived", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
                await EnsureTransactionColumnAsync(connection, "CloseoutPreparedAt", "TEXT NULL", cancellationToken);
                await EnsureTransactionColumnAsync(connection, "ClosedAt", "TEXT NULL", cancellationToken);
                await EnsureTransactionColumnAsync(connection, "CloseoutTechnician", "TEXT NOT NULL DEFAULT ''", cancellationToken);
                break;

            case 3:
                await ExecuteMigrationSqlAsync(connection,
                    """
                    CREATE TABLE IF NOT EXISTS FileArtifacts (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TransactionId TEXT NOT NULL,
                        ArtifactType TEXT NOT NULL,
                        Path TEXT NOT NULL,
                        Sha256 TEXT NOT NULL,
                        SizeBytes INTEGER NOT NULL,
                        CreatedAt TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS IX_FileArtifacts_TransactionId
                        ON FileArtifacts(TransactionId, CreatedAt DESC);

                    CREATE TABLE IF NOT EXISTS WorkflowOperations (
                        OperationId TEXT PRIMARY KEY,
                        TransactionId TEXT NOT NULL,
                        OperationType TEXT NOT NULL,
                        Stage TEXT NOT NULL,
                        Status TEXT NOT NULL,
                        StartedAt TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL,
                        LastError TEXT NOT NULL DEFAULT ''
                    );
                    """, cancellationToken);
                break;

            case 4:
                await ExecuteMigrationSqlAsync(connection,
                    """
                    CREATE INDEX IF NOT EXISTS IX_Devices_TransactionId ON Devices(TransactionId);
                    CREATE INDEX IF NOT EXISTS IX_Transactions_CustomerName
                        ON Transactions(CustomerLastName, CustomerFirstName);
                    CREATE INDEX IF NOT EXISTS IX_Technicians_LastUsedAt
                        ON Technicians(LastUsedAt DESC);
                    CREATE INDEX IF NOT EXISTS IX_Transactions_Archive
                        ON Transactions(IsArchived, IssuedAt DESC);

                    DELETE FROM FileArtifacts
                    WHERE Id NOT IN (
                        SELECT MIN(Id)
                        FROM FileArtifacts
                        GROUP BY TransactionId, ArtifactType, Path, Sha256
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS UX_FileArtifacts_Dedupe
                        ON FileArtifacts(TransactionId, ArtifactType, Path, Sha256);

                    UPDATE Devices SET Status = 'In shop' WHERE Status = 'Issued';

                    INSERT INTO Technicians (NameGrade, LastUsedAt, UseCount)
                    SELECT TRIM(Technician), MAX(IssuedAt), COUNT(*)
                    FROM Transactions
                    WHERE TRIM(Technician) <> ''
                    GROUP BY TRIM(Technician)
                    ON CONFLICT(NameGrade) DO UPDATE SET
                        LastUsedAt = CASE WHEN excluded.LastUsedAt > Technicians.LastUsedAt
                                          THEN excluded.LastUsedAt ELSE Technicians.LastUsedAt END,
                        UseCount = CASE WHEN excluded.UseCount > Technicians.UseCount
                                        THEN excluded.UseCount ELSE Technicians.UseCount END;
                    """, cancellationToken);
                break;

            case 5:
                await EnsureDeviceColumnAsync(
                    connection,
                    "PartNumber",
                    "TEXT NOT NULL DEFAULT ''",
                    cancellationToken);
                await ExecuteMigrationSqlAsync(connection,
                    """
                    UPDATE Devices SET PartNumber=Model WHERE TRIM(PartNumber)='';
                    CREATE INDEX IF NOT EXISTS IX_Devices_PartNumber
                        ON Devices(PartNumber COLLATE NOCASE);

                    CREATE TABLE IF NOT EXISTS ModelCatalog (
                        PartNumber TEXT PRIMARY KEY COLLATE NOCASE,
                        ModelName TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL,
                        LastUsedAt TEXT NOT NULL,
                        UseCount INTEGER NOT NULL DEFAULT 1
                    );
                    CREATE INDEX IF NOT EXISTS IX_ModelCatalog_ModelName
                        ON ModelCatalog(ModelName COLLATE NOCASE);
                    CREATE INDEX IF NOT EXISTS IX_ModelCatalog_LastUsedAt
                        ON ModelCatalog(LastUsedAt DESC);
                    """, cancellationToken);
                break;

            case 6:
                await ExecuteMigrationSqlAsync(connection,
                    """
                    CREATE TABLE IF NOT EXISTS PartialPickups (
                        Id TEXT PRIMARY KEY,
                        TransactionId TEXT NOT NULL,
                        ArtifactId INTEGER NOT NULL UNIQUE,
                        SequenceNumber INTEGER NOT NULL,
                        Technician TEXT NOT NULL,
                        Notes TEXT NOT NULL DEFAULT '',
                        PickedUpAt TEXT NOT NULL,
                        SignerName TEXT NOT NULL,
                        CertificateSubject TEXT NOT NULL DEFAULT '',
                        CertificateThumbprint TEXT NOT NULL DEFAULT '',
                        SignatureTime TEXT NULL,
                        CreatedAt TEXT NOT NULL,
                        FOREIGN KEY (TransactionId) REFERENCES Transactions(Id) ON DELETE CASCADE,
                        FOREIGN KEY (ArtifactId) REFERENCES FileArtifacts(Id) ON DELETE RESTRICT,
                        UNIQUE (TransactionId, SequenceNumber)
                    );
                    CREATE INDEX IF NOT EXISTS IX_PartialPickups_TransactionId
                        ON PartialPickups(TransactionId, SequenceNumber);

                    CREATE TABLE IF NOT EXISTS PartialPickupDevices (
                        PartialPickupId TEXT NOT NULL,
                        DeviceId INTEGER NOT NULL UNIQUE,
                        DeviceNumber INTEGER NOT NULL,
                        PRIMARY KEY (PartialPickupId, DeviceId),
                        FOREIGN KEY (PartialPickupId) REFERENCES PartialPickups(Id) ON DELETE CASCADE,
                        FOREIGN KEY (DeviceId) REFERENCES Devices(Id) ON DELETE RESTRICT
                    );
                    CREATE INDEX IF NOT EXISTS IX_PartialPickupDevices_PickupId
                        ON PartialPickupDevices(PartialPickupId, DeviceNumber);
                    """, cancellationToken);
                break;

            default:
                throw new InvalidOperationException($"Unsupported database migration version {version}.");
        }
    }

    private static async Task ExecuteMigrationSqlAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string> VerifyIntegrityAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        return Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) ?? "unknown";
    }

    private static async Task EnsureTransactionColumnAsync(
        SqliteConnection connection,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "Organization", "TicketNumber", "CustomerRank", "IsArchived",
            "CloseoutPreparedAt", "ClosedAt", "CloseoutTechnician"
        };
        if (!allowed.Contains(columnName))
        {
            throw new InvalidOperationException("Unsupported database migration column.");
        }

        await using var inspection = connection.CreateCommand();
        inspection.CommandText = "PRAGMA table_info(Transactions);";
        await using var reader = await inspection.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        await reader.DisposeAsync();

        await using var migration = connection.CreateCommand();
        migration.CommandText = $"ALTER TABLE Transactions ADD COLUMN {columnName} {columnDefinition};";
        await migration.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureDeviceColumnAsync(
        SqliteConnection connection,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(columnName, "PartNumber", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unsupported device migration column.");
        }

        await using var inspection = connection.CreateCommand();
        inspection.CommandText = "PRAGMA table_info(Devices);";
        await using var reader = await inspection.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        await reader.DisposeAsync();

        await using var migration = connection.CreateCommand();
        migration.CommandText = $"ALTER TABLE Devices ADD COLUMN {columnName} {columnDefinition};";
        await migration.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000; PRAGMA trusted_schema=OFF;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static async Task UpsertTechnicianAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string nameGrade,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken)
    {
        var normalized = nameGrade?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return;
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO Technicians(NameGrade,LastUsedAt,UseCount)
            VALUES($name,$usedAt,1)
            ON CONFLICT(NameGrade) DO UPDATE SET
                LastUsedAt=excluded.LastUsedAt,
                UseCount=Technicians.UseCount+1;
            """;
        command.Parameters.AddWithValue("$name", normalized);
        command.Parameters.AddWithValue("$usedAt", usedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAuditAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string transactionId,
        long? deviceId,
        string action,
        string details,
        string technician,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO Audit(TransactionId,DeviceId,Action,Details,Technician,ComputerName,ActionTime)
            VALUES($transactionId,$deviceId,$action,$details,$technician,$computer,$time);
            """;
        command.Parameters.AddWithValue("$transactionId", transactionId);
        command.Parameters.AddWithValue("$deviceId", (object?)deviceId ?? DBNull.Value);
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$details", details);
        command.Parameters.AddWithValue("$technician", technician?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("$computer", Environment.MachineName);
        command.Parameters.AddWithValue("$time", DateTimeOffset.Now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);


    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.MinValue;
}
