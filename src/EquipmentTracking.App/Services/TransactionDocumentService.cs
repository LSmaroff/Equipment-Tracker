using System.IO;
using EquipmentTracking.App.Models;

namespace EquipmentTracking.App.Services;

/// <summary>Builds disposable reference copies without rewriting signed evidence or database paths.</summary>
public sealed class TransactionDocumentService(
    DatabaseService database, SettingsService settings, PrintJobService printJobs)
{
    public async Task<string> CreateCurrentCopyAsync(string transactionId, bool forPrinting)
    {
        var transaction = await database.GetTransactionByIdAsync(transactionId)
            ?? throw new InvalidOperationException("The selected 1297 could not be found.");
        var devices = await database.GetTransactionDeviceStatusItemsAsync(transactionId);
        return await CreateCopyAsync(transaction.PdfPath, transaction.TicketNumber,
            devices.Where(device => device.IsAlreadyReturned).ToArray(), forPrinting);
    }

    public async Task<string> CreatePreservedCopyAsync(string transactionId, string path, bool forPrinting)
    {
        var transaction = await database.GetTransactionByIdAsync(transactionId)
            ?? throw new InvalidOperationException("The selected 1297 could not be found.");
        var receipts = await database.GetPickupReceiptsAsync(transactionId);
        var receipt = receipts.SingleOrDefault(item => string.Equals(item.PdfPath, path, StringComparison.OrdinalIgnoreCase));
        if (receipt is null)
        {
            var artifacts = await database.GetFileArtifactsAsync(transactionId);
            if (!artifacts.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)) &&
                !string.Equals(transaction.PdfPath, path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This PDF is not a document of the selected 1297.");
            // Originals and other historical evidence must not acquire later pickup marks.
            return await CreateCopyAsync(path, transaction.TicketNumber, [], forPrinting);
        }

        var devices = await database.GetTransactionDeviceStatusItemsAsync(transactionId);
        var selected = devices.Where(device => receipt.DeviceIds.Contains(device.DeviceId)).ToArray();
        if (selected.Length != receipt.DeviceIds.Count)
            throw new InvalidDataException("The pickup receipt's device links are incomplete.");
        return await CreateCopyAsync(path, transaction.TicketNumber, selected, forPrinting);
    }

    private Task<string> CreateCopyAsync(string path, string ticket,
        IReadOnlyList<TransactionDeviceStatusItem> devices, bool forPrinting)
    {
        var fields = devices.Select(device =>
        {
            if (!settings.Current.PdfFieldMappings.TryGetValue(device.PdfLogicalFieldName, out var field) ||
                string.IsNullOrWhiteSpace(field))
                throw new InvalidOperationException($"Missing PDF mapping for {device.PdfLogicalFieldName}.");
            return field.Trim();
        }).ToArray();
        if (fields.Distinct(StringComparer.OrdinalIgnoreCase).Count() != fields.Length)
            throw new InvalidOperationException("Returned devices map to the same PDF field. Correct the device mappings.");
        if (!forPrinting && fields.Length == 0) return Task.FromResult(path);
        return Task.Run(() => forPrinting
            ? printJobs.CreateTwoCopyLetterSheet(path, ticket, fields)
            : printJobs.CreateReadableStatusView(path, ticket, fields));
    }
}
