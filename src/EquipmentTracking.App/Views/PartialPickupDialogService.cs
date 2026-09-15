using System.Windows;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;
using EquipmentTracking.App.ViewModels;

namespace EquipmentTracking.App.Views;

/// <summary>Shares the signed pickup flow between Dashboard and device status updates.</summary>
public sealed class PartialPickupDialogService(
    DatabaseService database,
    TransactionWorkflowService workflow,
    AdobeService adobe)
{
    public async Task<PartialPickupResult?> SelectAndCompleteAsync(string transactionId)
    {
        var transaction = await database.GetTransactionByIdAsync(transactionId)
            ?? throw new InvalidOperationException("The selected 1297 could not be found.");
        if (transaction.IsArchived)
            throw new InvalidOperationException("This 1297 is already archived.");

        var devices = await database.GetTransactionDeviceStatusItemsAsync(transactionId);
        if (devices.All(device => !device.CanChange))
            throw new InvalidOperationException("All devices are already returned. Use Close out 1297 to archive this record.");
        var technicians = await database.GetTechnicianSuggestionsAsync();
        var viewModel = new DeviceStatusDialogViewModel(new RecentTransactionItem
        {
            TransactionId = transaction.Id,
            TicketNumber = transaction.TicketNumber,
            CustomerName = transaction.Customer.DisplayName,
            Organization = transaction.Organization,
            Technician = transaction.Technician
        }, devices, technicians.Select(item => item.NameGrade), pickupOnly: true);
        var selection = new DeviceStatusDialog
        {
            Owner = Application.Current.MainWindow,
            DataContext = viewModel
        };
        if (selection.ShowDialog() != true) return null;
        return await CompleteAsync(transaction.Id,
            viewModel.BuildUpdates().Where(update => update.MarkReturned).Select(update => update.DeviceId).ToArray(),
            viewModel.Technician, viewModel.Notes);
    }

    public async Task<PartialPickupResult?> CompleteAsync(
        string transactionId, IReadOnlyList<long> deviceIds, string technician, string notes)
    {
        var preparation = await workflow.BeginPartialPickupAsync(
            transactionId, deviceIds, technician, notes, DateTimeOffset.Now);
        var parent = preparation.Transaction;
        var displayTransaction = new EquipmentTransaction
        {
            Id = parent.Id,
            Customer = parent.Customer,
            Organization = parent.Organization,
            TicketNumber = parent.TicketNumber,
            Technician = technician.Trim(),
            PdfPath = preparation.PreparedPdfPath
        };
        var confirmation = new CloseoutDialog
        {
            Owner = Application.Current.MainWindow,
            DataContext = new CloseoutDialogViewModel(displayTransaction, [technician], adobe, isPartialPickup: true)
        };
        adobe.OpenPdf(preparation.PreparedPdfPath);
        if (confirmation.ShowDialog() != true)
        {
            MessageBox.Show(Application.Current.MainWindow,
                "No devices have been marked returned. This pickup copy is saved in Recovery. " +
                "You can reopen it, finish signing and resume, or roll back that attempt before choosing different devices.",
                "Pickup saved for later", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        return await workflow.FinalizePartialPickupAsync(preparation.OperationId);
    }

    public static string CompletionMessage(PartialPickupResult result) =>
        $"{result.UpdatedCount} device(s) returned. " +
        (result.Archived
            ? "All devices are returned; this 1297 and its pickup documents are now in Archive."
            : $"{result.RemainingCount} device(s) remain on this active 1297.") +
        "\n\nThe signed pickup copy is available under Documents for this record.";
}
