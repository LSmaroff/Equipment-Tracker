using System.Collections.ObjectModel;
using System.ComponentModel;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;

namespace EquipmentTracking.App.ViewModels;

public sealed class DeviceStatusDialogViewModel : ObservableObject
{
    private string _technician = string.Empty;
    private string _notes = string.Empty;
    private string _scanInput = string.Empty;
    private string _scanFeedback = "Scan a device barcode, serial number, or asset tag to select it for pickup.";
    private TransactionDeviceStatusItem? _selectedDevice;
    private readonly BarcodeParser _barcodeParser = new();

    public DeviceStatusDialogViewModel(
        RecentTransactionItem transaction,
        IEnumerable<TransactionDeviceStatusItem> devices,
        IEnumerable<string> technicianSuggestions,
        bool pickupOnly = false)
    {
        Transaction = transaction;
        Devices = new ObservableCollection<TransactionDeviceStatusItem>(devices);
        TechnicianSuggestions = new ObservableCollection<string>(technicianSuggestions);
        Technician = TechnicianSuggestions.FirstOrDefault() ?? transaction.Technician;
        IsPickupOnly = pickupOnly;
        SelectReadyCommand = new RelayCommand(SelectReadyDevices);
        ClearPickupSelectionCommand = new RelayCommand(ClearPickupSelection);
        SelectScannedDeviceCommand = new RelayCommand(() => SelectScannedDevice());
        foreach (var device in Devices)
        {
            device.PropertyChanged += Device_OnPropertyChanged;
        }
    }

    public RecentTransactionItem Transaction { get; }
    public ObservableCollection<TransactionDeviceStatusItem> Devices { get; }
    public ObservableCollection<string> TechnicianSuggestions { get; }
    public IReadOnlyList<string> StatusOptions => DeviceStatusCatalog.ActiveStatuses;
    public bool IsPickupOnly { get; }
    public bool CanEditWorkingStatus => !IsPickupOnly;
    public string Title => IsPickupOnly ? "Partial pickup" : "Update devices in 1297";
    public string SaveLabel => PickupCount > 0 ? "Prepare pickup copy" : "Save updates";
    public int PickupCount => Devices.Count(device => device.CanChange && device.IsPickedUp);
    public int RemainingCount => Devices.Count(device => device.CanChange && !device.IsPickedUp);
    public string PickupSummary => $"{PickupCount} selected for pickup · {RemainingCount} remaining after this pickup";
    public string PickupGuidance => RemainingCount == 0 && PickupCount > 0
        ? "After the customer's Pickup Signature is verified, these devices will be returned and this record will move to Archive with all its pickup documents."
        : "Prepare a separate 1297 with only these devices crossed out. Devices are returned only after the customer's Pickup Signature is verified; the rest stay open.";
    public string ScanInput
    {
        get => _scanInput;
        set => SetProperty(ref _scanInput, value ?? string.Empty);
    }
    public string ScanFeedback
    {
        get => _scanFeedback;
        private set => SetProperty(ref _scanFeedback, value);
    }
    public TransactionDeviceStatusItem? SelectedDevice
    {
        get => _selectedDevice;
        set => SetProperty(ref _selectedDevice, value);
    }
    public RelayCommand SelectReadyCommand { get; }
    public RelayCommand ClearPickupSelectionCommand { get; }
    public RelayCommand SelectScannedDeviceCommand { get; }

    public bool SelectScannedDevice()
    {
        var input = ScanInput.Trim();
        if (input.Length == 0)
        {
            return false;
        }

        if (input.Length > 4096)
        {
            ScanFeedback = "The scan is too long. Scan one device at a time.";
            return false;
        }

        var parsed = _barcodeParser.Parse(input);
        var matches = Devices.Where(device =>
            (parsed.Parsed && !string.IsNullOrWhiteSpace(parsed.SerialNumber)
                ? SameIdentifier(device.SerialNumber, parsed.SerialNumber)
                : SameIdentifier(device.SerialNumber, input) || SameIdentifier(device.AssetTag, input)) ||
            (parsed.Parsed && !string.IsNullOrWhiteSpace(parsed.AssetTag) &&
                SameIdentifier(device.AssetTag, parsed.AssetTag)))
            .ToArray();

        if (matches.Length != 1)
        {
            ScanFeedback = matches.Length == 0
                ? "No exact device match on this 1297. Check the serial or asset tag, or select its row."
                : "This identifier matches more than one device. Select the correct row manually.";
            return false;
        }

        var matched = matches[0];
        SelectedDevice = matched;
        if (!matched.CanChange)
        {
            ScanFeedback = $"Device {matched.DeviceNumber} ({matched.SerialNumber}) was already returned.";
            ScanInput = string.Empty;
            return false;
        }

        var alreadySelected = matched.IsPickedUp;
        matched.IsPickedUp = true;
        ScanFeedback = alreadySelected
            ? $"Device {matched.DeviceNumber} ({matched.SerialNumber}) is already selected; scan the next device."
            : $"Device {matched.DeviceNumber} ({matched.SerialNumber}) selected. Scan the next device.";
        ScanInput = string.Empty;
        return true;
    }

    private static bool SameIdentifier(string stored, string scanned) =>
        !string.IsNullOrWhiteSpace(stored) &&
        string.Equals(stored.Trim(), scanned.Trim(), StringComparison.OrdinalIgnoreCase);

    private void SelectReadyDevices()
    {
        foreach (var device in Devices.Where(device => device.CanChange &&
                     string.Equals(device.Status, DeviceStatusCatalog.ReadyForPickup, StringComparison.OrdinalIgnoreCase)))
        {
            device.IsPickedUp = true;
        }
        ScanFeedback = "Ready-for-pickup devices selected. Review the selection before saving.";
    }

    private void ClearPickupSelection()
    {
        foreach (var device in Devices.Where(device => device.CanChange))
        {
            device.IsPickedUp = false;
        }
        ScanFeedback = "Pickup selection cleared.";
    }

    private void Device_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TransactionDeviceStatusItem.IsPickedUp))
        {
            OnPropertyChanged(nameof(PickupCount));
            OnPropertyChanged(nameof(RemainingCount));
            OnPropertyChanged(nameof(PickupSummary));
            OnPropertyChanged(nameof(PickupGuidance));
            OnPropertyChanged(nameof(SaveLabel));
        }
    }

    public string Technician
    {
        get => _technician;
        set => SetProperty(ref _technician, value ?? string.Empty);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value ?? string.Empty);
    }

    public IReadOnlyList<DeviceStatusUpdate> BuildUpdates()
    {
        return Devices
            .Where(device => device.CanChange)
            .Where(device => device.IsPickedUp || (!IsPickupOnly &&
                             !string.Equals(
                                 device.Status,
                                 device.OriginalStatus,
                                 StringComparison.OrdinalIgnoreCase)))
            .Select(device => new DeviceStatusUpdate
            {
                DeviceId = device.DeviceId,
                Status = device.IsPickedUp
                    ? DeviceStatusCatalog.Returned
                    : device.Status,
                MarkReturned = device.IsPickedUp
            })
            .ToArray();
    }

    public IReadOnlyList<string> GetPickedUpLogicalFields()
    {
        return Devices
            .Where(device => device.CanChange && device.IsPickedUp)
            .Select(device => device.PdfLogicalFieldName)
            .ToArray();
    }
}
