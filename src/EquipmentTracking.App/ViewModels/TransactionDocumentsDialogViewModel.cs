using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;

namespace EquipmentTracking.App.ViewModels;

public sealed class TransactionDocumentsDialogViewModel : ObservableObject
{
    private TransactionDocumentItem? _selectedDocument;
    private readonly AdobeService _adobe;
    private readonly TransactionDocumentService? _documents;
    private readonly PrintJobService? _printJobs;
    private bool _isBusy;

    public TransactionDocumentsDialogViewModel(EquipmentTransaction transaction,
        IEnumerable<FileArtifactRecord> artifacts, IEnumerable<PickupReceipt> pickups,
        IReadOnlyList<TransactionDeviceStatusItem> devices, AdobeService adobe,
        TransactionDocumentService? documents = null, PrintJobService? printJobs = null)
    {
        Transaction = transaction;
        _adobe = adobe;
        _documents = documents;
        _printJobs = printJobs;
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pickup in pickups.OrderBy(item => item.SequenceNumber))
        {
            var selectedDevices = devices.Where(device => pickup.DeviceIds.Contains(device.DeviceId))
                .OrderBy(device => device.DeviceNumber).ToArray();
            Add(new TransactionDocumentItem
            {
                Name = $"Signed pickup {pickup.SequenceNumber}",
                Details = "Devices " + string.Join(", ", selectedDevices.Select(device =>
                    $"{device.DeviceNumber} ({device.SerialNumber})")),
                Signer = pickup.SignerName,
                Path = pickup.PdfPath,
                Date = pickup.PickedUpAt
            });
        }
        foreach (var artifact in artifacts.OrderBy(item => item.CreatedAt))
        {
            Add(new TransactionDocumentItem
            {
                Name = ArtifactLabel(artifact.ArtifactType),
                Details = artifact.ArtifactType == "OriginalSignedIntake"
                    ? "Original intake signature evidence — preserved unchanged"
                    : "Retained document for this 1297",
                Path = artifact.Path,
                Date = artifact.CreatedAt
            });
        }
        // A current cumulative view is distinct from the final pickup's subset
        // receipt even when both derive from the same preserved source path.
        Documents.Add(new TransactionDocumentItem
        {
            Name = transaction.IsArchived ? "Archived 1297" : "Current working 1297",
            IsCurrentStatus = true,
            Path = transaction.PdfPath,
            Date = transaction.ClosedAt ?? transaction.CreatedAt,
            Details = "Current status reference copy: all completed pickups crossed out"
        });
        OpenSelectedCommand = new AsyncRelayCommand(OpenSelectedAsync, () => !IsBusy && SelectedDocument is not null);
        OpenPreservedCommand = new RelayCommand(OpenPreserved, () => !IsBusy && SelectedDocument is not null);
        PrintSelectedCommand = new AsyncRelayCommand(PrintSelectedAsync,
            () => !IsBusy && SelectedDocument is not null && _documents is not null && _printJobs is not null);
        SelectedDocument = Documents.LastOrDefault();

        void Add(TransactionDocumentItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.Path) && seenPaths.Add(item.Path)) Documents.Add(item);
        }
    }

    public EquipmentTransaction Transaction { get; }
    public ObservableCollection<TransactionDocumentItem> Documents { get; } = [];
    public AsyncRelayCommand OpenSelectedCommand { get; }
    public RelayCommand OpenPreservedCommand { get; }
    public AsyncRelayCommand PrintSelectedCommand { get; }
    public string Summary => "View / print creates readable reference copies. Current status shows all completed pickups; each pickup receipt shows only its own devices. Open preserved PDF for unchanged signature evidence.";
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value)) RefreshCommands();
        }
    }
    public TransactionDocumentItem? SelectedDocument
    {
        get => _selectedDocument;
        set
        {
            if (SetProperty(ref _selectedDocument, value)) RefreshCommands();
        }
    }

    private void RefreshCommands()
    {
        OpenSelectedCommand.RaiseCanExecuteChanged();
        OpenPreservedCommand.RaiseCanExecuteChanged();
        PrintSelectedCommand.RaiseCanExecuteChanged();
    }

    private static string ArtifactLabel(string type) => type switch
    {
        "OriginalSignedIntake" => "Original signed intake",
        "FinalSignedCloseout" => "Final signed closeout",
        "SignedPartialPickup" => "Signed pickup copy",
        "AbandonedCloseoutAttempt" => "Preserved closeout attempt",
        "AbandonedPartialPickupAttempt" => "Preserved pickup attempt",
        "AbandonedSignedPartialPickupAttempt" => "Preserved signed pickup attempt",
        _ when type.StartsWith("WorkingStatus", StringComparison.Ordinal) => "Working 1297 copy",
        _ => type
    };

    private void OpenPreserved()
    {
        if (SelectedDocument is null) return;
        try { _adobe.OpenPdf(SelectedDocument.Path); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Open 1297 document", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private Task<string> CreateSelectedCopyAsync(TransactionDocumentItem selected, bool forPrinting) =>
        selected.IsCurrentStatus
            ? _documents!.CreateCurrentCopyAsync(Transaction.Id, forPrinting)
            : _documents!.CreatePreservedCopyAsync(Transaction.Id, selected.Path, forPrinting);

    private async Task OpenSelectedAsync()
    {
        if (SelectedDocument is null) return;
        try
        {
            IsBusy = true;
            var path = _documents is null ? SelectedDocument.Path
                : await CreateSelectedCopyAsync(SelectedDocument, forPrinting: false);
            _adobe.OpenPdf(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "View 1297", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsBusy = false; }
    }

    private async Task PrintSelectedAsync()
    {
        if (SelectedDocument is null || _documents is null || _printJobs is null) return;
        string? path = null;
        try
        {
            IsBusy = true;
            path = await CreateSelectedCopyAsync(SelectedDocument, forPrinting: true);
            var printDialogRequested = _adobe.OpenPdfForPrinting(path);
            MessageBox.Show(
                (printDialogRequested ? "The print dialog is open. " : "Press Ctrl+P in the PDF viewer. ") +
                "Use US Letter, portrait, one-sided printing, and a copy count of 1 for two readable copies. " +
                "Keep this message open until printing finishes; closing it removes the temporary print sheet. " +
                "The preserved signed PDF is unchanged.",
                "1297 print sheet opened", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Print 1297 document", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            try
            {
                if (path is not null) await _printJobs.DeleteTemporaryPrintJobAsync(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Temporary print copy cleanup", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { IsBusy = false; }
        }
    }
}

public sealed class TransactionDocumentItem
{
    public bool IsCurrentStatus { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public string Signer { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public DateTimeOffset Date { get; init; }
    public string DisplayDate => Date.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
    public string Availability => File.Exists(Path) ? "Available" : "File not found";
}
