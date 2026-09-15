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

    public TransactionDocumentsDialogViewModel(EquipmentTransaction transaction,
        IEnumerable<FileArtifactRecord> artifacts, IEnumerable<PickupReceipt> pickups,
        IReadOnlyList<TransactionDeviceStatusItem> devices, AdobeService adobe)
    {
        Transaction = transaction;
        _adobe = adobe;
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
        Add(new TransactionDocumentItem
        {
            Name = transaction.IsArchived ? "Archived 1297" : "Current working 1297",
            Path = transaction.PdfPath,
            Date = transaction.ClosedAt ?? transaction.CreatedAt,
            Details = "Current document shown by Open PDF"
        });
        OpenSelectedCommand = new RelayCommand(OpenSelected, () => SelectedDocument is not null);
        SelectedDocument = Documents.FirstOrDefault();

        void Add(TransactionDocumentItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.Path) && seenPaths.Add(item.Path)) Documents.Add(item);
        }
    }

    public EquipmentTransaction Transaction { get; }
    public ObservableCollection<TransactionDocumentItem> Documents { get; } = [];
    public RelayCommand OpenSelectedCommand { get; }
    public string Summary => $"{Documents.Count} document(s) under this 1297. Pickup copies remain separate; the original signed intake is unchanged.";
    public TransactionDocumentItem? SelectedDocument
    {
        get => _selectedDocument;
        set
        {
            if (SetProperty(ref _selectedDocument, value)) OpenSelectedCommand.RaiseCanExecuteChanged();
        }
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

    private void OpenSelected()
    {
        if (SelectedDocument is null) return;
        try { _adobe.OpenPdf(SelectedDocument.Path); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Open 1297 document", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

public sealed class TransactionDocumentItem
{
    public string Name { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public string Signer { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public DateTimeOffset Date { get; init; }
    public string DisplayDate => Date.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
    public string Availability => File.Exists(Path) ? "Available" : "File not found";
}
