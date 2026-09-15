using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;
using EquipmentTracking.App.Views;

namespace EquipmentTracking.App.ViewModels;

public sealed class IntakeViewModel : ObservableObject
{
    private readonly TransactionWorkflowService _workflow;
    private readonly CacCertificateService _cacCertificates;
    private readonly BarcodeParser _barcodeParser;
    private readonly DeviceRecognitionService _deviceRecognition;
    private readonly AdobeService _adobe;
    private readonly SettingsService _settings;
    private readonly DatabaseService _database;
    private readonly ExcelExportService _excel;
    private readonly StatusService _status;
    private readonly FileLogger _logger;

    private CacCertificateCandidate? _selectedCacCertificate;
    private DeviceRecord? _selectedDevice;
    private WorkingTransaction? _workingTransaction;
    private string _rank = string.Empty;
    private string _firstName = string.Empty;
    private string _middleInitial = string.Empty;
    private string _lastName = string.Empty;
    private string _phoneNumber = string.Empty;
    private string _technician = string.Empty;
    private string _selectedOrganization = string.Empty;
    private string _ticketNumber = string.Empty;
    private string _currentPdfPath = string.Empty;
    private string _workflowStatus = "Create a new 1297 to begin.";
    private bool _isBusy;
    private bool _isPdfPrepared;

    public IntakeViewModel(
        TransactionWorkflowService workflow,
        CacCertificateService cacCertificates,
        BarcodeParser barcodeParser,
        DeviceRecognitionService deviceRecognition,
        AdobeService adobe,
        SettingsService settings,
        DatabaseService database,
        ExcelExportService excel,
        StatusService status,
        FileLogger logger)
    {
        _workflow = workflow;
        _cacCertificates = cacCertificates;
        _barcodeParser = barcodeParser;
        _deviceRecognition = deviceRecognition;
        _adobe = adobe;
        _settings = settings;
        _database = database;
        _excel = excel;
        _status = status;
        _logger = logger;

        RefreshCacCommand = new AsyncRelayCommand(
            RefreshCacAsync,
            () => CanEditTransactionData);
        CommitDeviceEntryCommand = new AsyncRelayCommand(CommitDeviceEntryFromCommandAsync);
        RemoveDeviceCommand = new RelayCommand(
            RemoveSelectedDevice,
            () => CanEnterDevices && SelectedDevice is not null);
        StartTransactionCommand = new AsyncRelayCommand(
            StartTransactionAsync,
            () => !IsBusy && _workingTransaction is null);
        PrepareAndOpenCommand = new AsyncRelayCommand(PrepareAndOpenAsync, CanPrepareAndOpen);
        FinalizeCommand = new AsyncRelayCommand(FinalizeAsync, CanFinalize);
        OpenCurrentPdfCommand = new RelayCommand(
            OpenCurrentPdf,
            () => !IsBusy && File.Exists(CurrentPdfPath));
        ResetCommand = new RelayCommand(Reset, () => !IsBusy);

        ReloadOrganizations();
        EnsurePendingDeviceEntry();
        _ = RefreshTechnicianSuggestionsAsync();
    }

    public IReadOnlyList<string> RankOptions => RankCatalog.Values;
    public ObservableCollection<CacCertificateCandidate> CacCertificates { get; } = [];
    public ObservableCollection<string> Organizations { get; } = [];
    public ObservableCollection<string> TechnicianSuggestions { get; } = [];
    public ObservableCollection<DeviceEntryRow> DeviceEntries { get; } = [];
    public ObservableCollection<DeviceRecord> Devices { get; } = [];

    public CacCertificateCandidate? SelectedCacCertificate
    {
        get => _selectedCacCertificate;
        set
        {
            if (SetProperty(ref _selectedCacCertificate, value) && value is not null)
            {
                ApplyIdentity(value.Identity);
            }
        }
    }

    public DeviceRecord? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                RemoveDeviceCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Rank
    {
        get => _rank;
        set => SetProperty(ref _rank, RankCatalog.Normalize(value));
    }

    public string FirstName
    {
        get => _firstName;
        set
        {
            if (SetProperty(ref _firstName, value?.Trim() ?? string.Empty))
            {
                RaiseWorkflowCanExecuteChanged();
            }
        }
    }

    public string MiddleInitial
    {
        get => _middleInitial;
        set => SetProperty(
            ref _middleInitial,
            string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim()[..1].ToUpperInvariant());
    }

    public string LastName
    {
        get => _lastName;
        set
        {
            if (SetProperty(ref _lastName, value?.Trim() ?? string.Empty))
            {
                RaiseWorkflowCanExecuteChanged();
            }
        }
    }

    public string PhoneNumber
    {
        get => _phoneNumber;
        set
        {
            if (SetProperty(ref _phoneNumber, value ?? string.Empty))
            {
                RaiseWorkflowCanExecuteChanged();
            }
        }
    }

    public string Technician
    {
        get => _technician;
        set
        {
            if (SetProperty(ref _technician, value ?? string.Empty))
            {
                RaiseWorkflowCanExecuteChanged();
            }
        }
    }

    public string SelectedOrganization
    {
        get => _selectedOrganization;
        set
        {
            if (SetProperty(ref _selectedOrganization, value ?? string.Empty))
            {
                RaiseWorkflowCanExecuteChanged();
            }
        }
    }

    public string TicketNumber
    {
        get => _ticketNumber;
        set
        {
            if (SetProperty(ref _ticketNumber, value ?? string.Empty))
            {
                RaiseWorkflowCanExecuteChanged();
            }
        }
    }

    public string CurrentPdfPath
    {
        get => _currentPdfPath;
        private set
        {
            if (SetProperty(ref _currentPdfPath, value))
            {
                OpenCurrentPdfCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string WorkflowStatus
    {
        get => _workflowStatus;
        private set => SetProperty(ref _workflowStatus, value);
    }

    public int DeviceCount => Devices.Count;

    public bool HasDevices => Devices.Count > 0;

    public bool HasNoDevices => !HasDevices;

    public bool HasActiveTransaction => _workingTransaction is not null;

    public bool IsPdfPrepared => _isPdfPrepared;

    public bool IsCreateStage => !HasActiveTransaction;

    public bool IsDeviceStage => HasActiveTransaction && !HasDevices;

    public bool IsDetailsStage =>
        HasActiveTransaction && HasDevices && !HasRequiredTransactionData();

    public bool IsSignatureStage =>
        HasActiveTransaction && HasDevices && HasRequiredTransactionData();

    public string WorkflowStageSummary
    {
        get
        {
            if (!HasActiveTransaction)
            {
                return "Step 1 of 4 · Create a working 1297";
            }

            if (!HasDevices)
            {
                return "Step 2 of 4 · Scan or enter equipment";
            }

            if (!HasRequiredTransactionData())
            {
                return "Step 3 of 4 · Complete customer and ticket details";
            }

            return IsPdfPrepared
                ? "Step 4 of 4 · Save Adobe, then finalize the signed PDF"
                : "Step 4 of 4 · Prepare and open the PDF for signatures";
        }
    }

    public bool CanEditTransactionData => !IsBusy && !_isPdfPrepared;

    public bool CanEnterDevices => CanEditTransactionData && HasActiveTransaction;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanEditTransactionData));
                OnPropertyChanged(nameof(CanEnterDevices));
                RaiseWorkflowCanExecuteChanged();
                RefreshCacCommand.RaiseCanExecuteChanged();
                RemoveDeviceCommand.RaiseCanExecuteChanged();
                OpenCurrentPdfCommand.RaiseCanExecuteChanged();
                ResetCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand RefreshCacCommand { get; }
    public AsyncRelayCommand CommitDeviceEntryCommand { get; }
    public RelayCommand RemoveDeviceCommand { get; }
    public AsyncRelayCommand StartTransactionCommand { get; }
    public AsyncRelayCommand PrepareAndOpenCommand { get; }
    public AsyncRelayCommand FinalizeCommand { get; }
    public RelayCommand OpenCurrentPdfCommand { get; }
    public RelayCommand ResetCommand { get; }

    public async Task<bool> ProcessScanAsync(DeviceEntryRow? entry)
    {
        if (entry is null || !CanEnterDevices)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(entry.ScanInput))
        {
            var scan = entry.ScanInput.Length > 4096
                ? entry.ScanInput[..4096]
                : entry.ScanInput;
            var result = _barcodeParser.Parse(scan);
            if (result.Parsed)
            {
                var recognition = await _deviceRecognition.RecognizeAsync(
                    result,
                    entry.PartNumber);
                if (!string.IsNullOrWhiteSpace(result.PartNumber)) entry.PartNumber = result.PartNumber;
                else if (string.IsNullOrWhiteSpace(entry.PartNumber) &&
                         !string.IsNullOrWhiteSpace(recognition.PartNumber))
                {
                    entry.PartNumber = recognition.PartNumber;
                }
                if (!string.IsNullOrWhiteSpace(result.SerialNumber)) entry.SerialNumber = result.SerialNumber;
                if (!string.IsNullOrWhiteSpace(result.AssetTag)) entry.AssetTag = result.AssetTag;
                if (string.IsNullOrWhiteSpace(entry.ModelName) &&
                    !string.IsNullOrWhiteSpace(recognition.ModelName))
                {
                    entry.ModelName = recognition.ModelName;
                }
            }
            entry.Feedback = result.Message;
        }

        return await TryCommitDeviceEntryAsync(entry, entry.ScanInput);
    }

    public Task<bool> CommitOrUpdateEntryAsync(DeviceEntryRow? entry)
    {
        return entry is null
            ? Task.FromResult(false)
            : TryCommitDeviceEntryAsync(entry, entry.ScanInput);
    }

    private async Task CommitDeviceEntryFromCommandAsync(object? parameter)
    {
        if (parameter is DeviceEntryRow entry)
        {
            await TryCommitDeviceEntryAsync(entry, entry.ScanInput);
        }
    }

    private async Task<bool> TryCommitDeviceEntryAsync(DeviceEntryRow entry, string rawScanValue)
    {
        if (!CanEnterDevices)
        {
            return false;
        }

        var maximumDevices = Math.Max(1, _settings.Current.MaximumDevicesPerForm);
        if (!entry.IsCommitted && Devices.Count >= maximumDevices)
        {
            return SetEntryError(entry, $"The form is limited to {maximumDevices} devices.");
        }

        var partNumber = entry.PartNumber.Trim();
        var modelName = entry.ModelName.Trim();
        var serial = entry.SerialNumber.Trim();
        var asset = entry.AssetTag.Trim();
        if (string.IsNullOrWhiteSpace(partNumber) || string.IsNullOrWhiteSpace(serial))
        {
            return SetEntryError(entry, "A part number and serial number are required.");
        }

        var duplicateInForm = Devices.Any(device =>
            !ReferenceEquals(device, entry.CommittedDevice) &&
            string.Equals(device.SerialNumber, serial, StringComparison.OrdinalIgnoreCase));
        if (duplicateInForm)
        {
            return SetEntryError(entry, $"Serial number '{serial}' is already in this transaction.");
        }

        if (await _database.IsActiveSerialNumberAsync(serial))
        {
            return SetEntryError(
                entry,
                $"Serial number '{serial}' is already assigned to a current 1297 and was not saved.");
        }

        if (entry.CommittedDevice is not null &&
            !string.Equals(
                entry.CommittedDevice.PartNumber,
                partNumber,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                entry.CommittedDevice.Model,
                modelName,
                StringComparison.OrdinalIgnoreCase))
        {
            modelName = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(modelName))
        {
            modelName = await _database.ResolveModelNameAsync(partNumber) ?? string.Empty;
        }

        if (entry.IsCommitted && entry.CommittedDevice is not null)
        {
            entry.CommittedDevice.PartNumber = partNumber;
            entry.CommittedDevice.Model = modelName;
            entry.CommittedDevice.SerialNumber = serial;
            entry.CommittedDevice.AssetTag = asset;
            if (!string.IsNullOrWhiteSpace(rawScanValue))
            {
                entry.CommittedDevice.RawScanValue = rawScanValue;
            }
            entry.Feedback = "Device entry updated.";
            WorkflowStatus = $"Device {entry.RowNumber} updated.";
        }
        else
        {
            var device = new DeviceRecord
            {
                PartNumber = partNumber,
                Model = modelName,
                SerialNumber = serial,
                AssetTag = asset,
                RawScanValue = rawScanValue ?? string.Empty,
                Status = DeviceStatusCatalog.InShop
            };
            Devices.Add(device);
            entry.CommittedDevice = device;
            entry.IsCommitted = true;
            entry.Feedback = string.IsNullOrWhiteSpace(rawScanValue)
                ? "Device added manually. Fields remain editable."
                : "Device captured. Raw scan stored; fields remain editable.";
            WorkflowStatus = $"Device {Devices.Count} captured. The next entry row is ready.";
            EnsurePendingDeviceEntry();
        }

        entry.PartNumber = partNumber;
        entry.ModelName = modelName;
        entry.SerialNumber = serial;
        entry.AssetTag = asset;
        _status.Message = WorkflowStatus;
        OnPropertyChanged(nameof(DeviceCount));
        RaiseWorkflowCanExecuteChanged();
        return true;
    }

    private bool SetEntryError(DeviceEntryRow entry, string message)
    {
        entry.Feedback = message;
        WorkflowStatus = message;
        _status.Message = message;
        return false;
    }

    private Task RefreshCacAsync()
    {
        try
        {
            IsBusy = true;
            CacCertificates.Clear();

            foreach (var candidate in _cacCertificates.GetCandidates())
            {
                CacCertificates.Add(candidate);
            }

            SelectedCacCertificate = CacCertificates.FirstOrDefault();

            WorkflowStatus = CacCertificates.Count == 0
                ? "No usable smart-card certificate was found. Confirm the CAC is inserted."
                : $"Found {CacCertificates.Count} certificate candidate(s). Verify the selected customer name.";

            _status.Message = WorkflowStatus;
        }
        catch (Exception ex)
        {
            ShowError("Read CAC certificates", ex);
        }
        finally
        {
            IsBusy = false;
        }

        return Task.CompletedTask;
    }

    private void RemoveSelectedDevice()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var serial = SelectedDevice.SerialNumber;
        Devices.Remove(SelectedDevice);
        SelectedDevice = null;

        var matchingEntry = DeviceEntries.FirstOrDefault(entry =>
            entry.IsCommitted &&
            string.Equals(entry.SerialNumber, serial, StringComparison.OrdinalIgnoreCase));

        if (matchingEntry is not null)
        {
            DeviceEntries.Remove(matchingEntry);
        }

        RenumberDeviceEntries();
        EnsurePendingDeviceEntry();
        OnPropertyChanged(nameof(DeviceCount));
        RaiseWorkflowCanExecuteChanged();

        WorkflowStatus = $"Device removed. {Devices.Count} device(s) remain.";
        _status.Message = WorkflowStatus;
    }

    private async Task StartTransactionAsync()
    {
        try
        {
            IsBusy = true;
            ReloadOrganizations();
            await RefreshTechnicianSuggestionsAsync();
            _workingTransaction = _workflow.StartTransaction();
            CurrentPdfPath = _workingTransaction.TemplateCopyPath;
            OnPropertyChanged(nameof(HasActiveTransaction));
            OnPropertyChanged(nameof(CanEnterDevices));
            WorkflowStatus =
                $"New 1297 {_workingTransaction.TransactionId} created. Scan all devices first.";
            _status.Message = WorkflowStatus;
        }
        catch (Exception ex)
        {
            ShowError("Create new 1297", ex);
        }
        finally
        {
            IsBusy = false;
            RaiseWorkflowCanExecuteChanged();
        }

        await Task.CompletedTask;
    }

    private bool CanPrepareAndOpen(object? parameter)
    {
        return !IsBusy &&
               !_isPdfPrepared &&
               _workingTransaction is not null &&
               HasRequiredTransactionData() &&
               Devices.Count > 0;
    }

    private async Task PrepareAndOpenAsync(object? parameter)
    {
        if (_workingTransaction is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var result = _workflow.PreparePdf(
                _workingTransaction,
                BuildCustomerIdentity(),
                PhoneNumber,
                Technician,
                SelectedOrganization,
                TicketNumber,
                Devices);

            CurrentPdfPath = _workingTransaction.PreparedPdfPath;
            _adobe.OpenPdf(CurrentPdfPath);
            _isPdfPrepared = true;
            OnPropertyChanged(nameof(CanEditTransactionData));
            OnPropertyChanged(nameof(CanEnterDevices));
            RaiseWorkflowCanExecuteChanged();
            RefreshCacCommand.RaiseCanExecuteChanged();
            RemoveDeviceCommand.RaiseCanExecuteChanged();

            var missingMessage = result.MissingPdfFields.Count > 0
                ? $" Missing PDF fields: {string.Join(", ", result.MissingPdfFields)}."
                : string.Empty;

            WorkflowStatus =
                $"Prepared {result.FieldsFilled} field(s). Complete the signatures in Adobe, save, then select Finalize.{missingMessage}";
            _status.Message = "PDF opened for signatures.";
        }
        catch (Exception ex)
        {
            ShowError("Prepare PDF", ex);
        }
        finally
        {
            IsBusy = false;
            RaiseWorkflowCanExecuteChanged();
        }

        await Task.CompletedTask;
    }

    private bool CanFinalize(object? parameter)
    {
        return !IsBusy &&
               _isPdfPrepared &&
               _workingTransaction is not null &&
               File.Exists(_workingTransaction.PreparedPdfPath) &&
               HasRequiredTransactionData() &&
               Devices.Count > 0;
    }

    private async Task FinalizeAsync(object? parameter)
    {
        if (_workingTransaction is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            WorkflowStatus = "Reading the PDF signature and saving the transaction...";
            _status.Message = WorkflowStatus;

            var result = await _workflow.FinalizeAsync(
                _workingTransaction,
                BuildCustomerIdentity(),
                PhoneNumber,
                Technician,
                SelectedOrganization,
                TicketNumber,
                Devices,
                SelectedCacCertificate);

            await CaptureModelCatalogMappingsAsync(result.Transaction);

            WorkflowStatus =
                $"Completed {result.Transaction.Id}. Saved to {result.FinalPdfPath}";
            _status.Message = "Transaction finalized.";

            MessageBox.Show(
                $"Transaction completed.\n\nPDF:\n{result.FinalPdfPath}\n\n" +
                (string.IsNullOrWhiteSpace(result.ExcelExportPath)
                    ? "The database was saved. Excel export needs to be retried."
                    : $"Excel:\n{result.ExcelExportPath}"),
                "Intake completed",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Reset();
            await RefreshTechnicianSuggestionsAsync();
        }
        catch (SignatureValidationException ex)
        {
            _logger.Error("Finalize transaction signature validation failed.", ex);
            WorkflowStatus = ex.Message;
            _status.Message = "The intake signature could not be verified. Review the diagnostic details.";
            DiagnosticDetailsDialog.Show(
                "Intake signature could not be verified",
                ex.Message,
                ex.Diagnostics.ToDisplayText());
        }
        catch (Exception ex)
        {
            ShowError("Finalize transaction", ex);
        }
        finally
        {
            IsBusy = false;
            RaiseWorkflowCanExecuteChanged();
        }
    }

    private async Task CaptureModelCatalogMappingsAsync(EquipmentTransaction transaction)
    {
        var mappingSaved = false;
        foreach (var device in transaction.Devices
                     .Where(item => !string.IsNullOrWhiteSpace(item.PartNumber))
                     .GroupBy(item => item.PartNumber, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group
                         .OrderByDescending(item => !string.IsNullOrWhiteSpace(item.Model))
                         .First()))
        {
            var modelName = device.Model.Trim();
            if (string.IsNullOrWhiteSpace(modelName))
            {
                var dialog = new ModelNameDialog(device.PartNumber)
                {
                    Owner = Application.Current.MainWindow
                };
                if (dialog.ShowDialog() != true)
                {
                    continue;
                }

                modelName = dialog.ModelName;
            }

            try
            {
                await _database.UpsertModelCatalogEntryAsync(
                    device.PartNumber,
                    modelName,
                    Technician,
                    transaction.Id);
                mappingSaved = true;
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    $"The model mapping for part number '{device.PartNumber}' could not be saved: {ex.Message}");
                MessageBox.Show(
                    $"The 1297 was finalized, but the common model name for part number '{device.PartNumber}' could not be saved.\n\n{ex.Message}",
                    "Model name not saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        if (mappingSaved)
        {
            try
            {
                await _excel.ExportAsync();
            }
            catch (IOException ex)
            {
                _logger.Warning(
                    $"Model mappings were saved, but the Excel export could not be refreshed: {ex.Message}");
            }
        }
    }

    private void OpenCurrentPdf()
    {
        if (string.IsNullOrWhiteSpace(CurrentPdfPath) || !File.Exists(CurrentPdfPath))
        {
            return;
        }

        try
        {
            _adobe.OpenPdf(CurrentPdfPath);
            _status.Message = "Current working PDF opened.";
        }
        catch (Exception ex)
        {
            ShowError("Open current PDF", ex);
        }
    }

    private void Reset()
    {
        _workingTransaction = null;
        _isPdfPrepared = false;
        OnPropertyChanged(nameof(HasActiveTransaction));
        OnPropertyChanged(nameof(CanEditTransactionData));
        OnPropertyChanged(nameof(CanEnterDevices));
        CurrentPdfPath = string.Empty;
        Rank = string.Empty;
        FirstName = string.Empty;
        MiddleInitial = string.Empty;
        LastName = string.Empty;
        PhoneNumber = string.Empty;
        TicketNumber = string.Empty;
        CacCertificates.Clear();
        SelectedCacCertificate = null;
        Devices.Clear();
        DeviceEntries.Clear();
        SelectedDevice = null;
        ReloadOrganizations();
        EnsurePendingDeviceEntry();
        OnPropertyChanged(nameof(DeviceCount));
        RaiseWorkflowCanExecuteChanged();

        WorkflowStatus = "Ready. Create a new 1297 to begin.";
        _status.Message = WorkflowStatus;
    }

    private CustomerIdentity BuildCustomerIdentity()
    {
        return new CustomerIdentity
        {
            Rank = Rank.Trim(),
            FirstName = FirstName.Trim(),
            MiddleInitial = MiddleInitial.Trim(),
            LastName = LastName.Trim(),
            OriginalName = SelectedCacCertificate?.Identity.OriginalName ?? string.Empty
        };
    }

    private void ApplyIdentity(CustomerIdentity identity)
    {
        if (!identity.HasUsableName)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(identity.Rank))
        {
            Rank = identity.Rank;
        }
        FirstName = identity.FirstName;
        MiddleInitial = identity.MiddleInitial;
        LastName = identity.LastName;
        WorkflowStatus = $"CAC identity detected: {identity.DisplayName}. Verify the rank and name before continuing.";
    }

    private bool HasRequiredTransactionData()
    {
        return !string.IsNullOrWhiteSpace(FirstName) &&
               !string.IsNullOrWhiteSpace(LastName) &&
               !string.IsNullOrWhiteSpace(PhoneNumber) &&
               !string.IsNullOrWhiteSpace(Technician) &&
               !string.IsNullOrWhiteSpace(SelectedOrganization) &&
               !string.IsNullOrWhiteSpace(TicketNumber);
    }

    private async Task RefreshTechnicianSuggestionsAsync()
    {
        try
        {
            var suggestions = await _database.GetTechnicianSuggestionsAsync();
            TechnicianSuggestions.Clear();
            foreach (var suggestion in suggestions)
            {
                TechnicianSuggestions.Add(suggestion.NameGrade);
            }

            if (string.IsNullOrWhiteSpace(Technician) && TechnicianSuggestions.Count > 0)
            {
                Technician = TechnicianSuggestions[0];
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Technician suggestions could not be loaded: {ex.Message}");
        }
    }

    private void ReloadOrganizations()
    {
        var previous = SelectedOrganization;
        Organizations.Clear();

        foreach (var organization in _settings.Current.Organizations
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Organizations.Add(organization);
        }

        SelectedOrganization = Organizations.FirstOrDefault(value =>
                string.Equals(value, previous, StringComparison.OrdinalIgnoreCase))
            ?? Organizations.FirstOrDefault()
            ?? string.Empty;
    }

    private void EnsurePendingDeviceEntry()
    {
        var maximumDevices = Math.Max(1, _settings.Current.MaximumDevicesPerForm);
        if (Devices.Count >= maximumDevices || DeviceEntries.Any(entry => !entry.IsCommitted))
        {
            return;
        }

        DeviceEntries.Add(new DeviceEntryRow
        {
            RowNumber = DeviceEntries.Count + 1
        });
    }

    private void RenumberDeviceEntries()
    {
        for (var index = 0; index < DeviceEntries.Count; index++)
        {
            DeviceEntries[index].RowNumber = index + 1;
        }
    }

    private void RaiseWorkflowCanExecuteChanged()
    {
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(HasNoDevices));
        OnPropertyChanged(nameof(IsPdfPrepared));
        OnPropertyChanged(nameof(IsCreateStage));
        OnPropertyChanged(nameof(IsDeviceStage));
        OnPropertyChanged(nameof(IsDetailsStage));
        OnPropertyChanged(nameof(IsSignatureStage));
        OnPropertyChanged(nameof(WorkflowStageSummary));
        RemoveDeviceCommand.RaiseCanExecuteChanged();
        RefreshCacCommand.RaiseCanExecuteChanged();
        StartTransactionCommand.RaiseCanExecuteChanged();
        PrepareAndOpenCommand.RaiseCanExecuteChanged();
        FinalizeCommand.RaiseCanExecuteChanged();
        OpenCurrentPdfCommand.RaiseCanExecuteChanged();
    }

    private void ShowError(string title, Exception exception)
    {
        _logger.Error(title, exception);
        WorkflowStatus = exception.Message;
        _status.Message = $"{title} failed.";

        MessageBox.Show(
            exception.Message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
