using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;
using EquipmentTracking.App.Views;

namespace EquipmentTracking.App.ViewModels;

public sealed class ReturnsViewModel : ObservableObject
{
    private readonly DatabaseService _database;
    private readonly ExcelExportService _excel;
    private readonly AdobeService _adobe;
    private readonly TransactionDocumentService _documents;
    private readonly TransactionWorkflowService _workflow;
    private readonly RecordCodeService _recordCodes;
    private readonly StatusService _status;
    private readonly FileLogger _logger;

    private string _searchQuery = string.Empty;
    private DeviceSearchResult? _selectedResult;
    private string _technician = string.Empty;
    private string _selectedStatus = DeviceStatusCatalog.InShop;
    private string _notes = string.Empty;
    private bool _isBusy;

    public ReturnsViewModel(
        DatabaseService database,
        ExcelExportService excel,
        AdobeService adobe,
        TransactionWorkflowService workflow,
        RecordCodeService recordCodes,
        StatusService status,
        FileLogger logger,
        TransactionDocumentService documents)
    {
        _database = database;
        _excel = excel;
        _adobe = adobe;
        _documents = documents;
        _workflow = workflow;
        _recordCodes = recordCodes;
        _status = status;
        _logger = logger;

        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy);
        UpdateStatusCommand = new AsyncRelayCommand(
            UpdateStatusAsync,
            () => !IsBusy &&
                  SelectedResult is not null &&
                  !string.Equals(
                      SelectedResult.Status,
                      DeviceStatusCatalog.Returned,
                      StringComparison.OrdinalIgnoreCase));
        UpdateTransactionDevicesCommand = new AsyncRelayCommand(
            UpdateTransactionDevicesAsync,
            () => !IsBusy && SelectedResult is not null);
        OpenPdfCommand = new AsyncRelayCommand(
            OpenPdfAsync,
            () => !IsBusy && SelectedResult is not null);
    }

    public ObservableCollection<DeviceSearchResult> Results { get; } = [];
    public ObservableCollection<string> TechnicianSuggestions { get; } = [];
    public IReadOnlyList<string> StatusOptions => DeviceStatusCatalog.ActiveStatuses;

    public bool HasResults => Results.Count > 0;
    public bool HasNoResults => !HasResults;
    public bool HasSelection => SelectedResult is not null;

    public string SelectedResultSummary => SelectedResult is null
        ? "Select a device row to review and update its status."
        : $"Ticket {SelectedResult.TicketNumber} · {SelectedResult.SerialNumber} · {SelectedResult.Status}";

    public string EmptyResultsMessage => string.IsNullOrWhiteSpace(SearchQuery)
        ? "No equipment records are available yet. Finalized intake records appear here."
        : "No equipment records match the current search. Check the ticket, serial, customer, organization, or status and try again.";

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(EmptyResultsMessage));
            }
        }
    }

    public DeviceSearchResult? SelectedResult
    {
        get => _selectedResult;
        set
        {
            if (!SetProperty(ref _selectedResult, value))
            {
                return;
            }

            foreach (var result in Results)
            {
                result.IsSameTransactionAsSelection = value is not null &&
                    string.Equals(
                        result.TransactionId,
                        value.TransactionId,
                        StringComparison.OrdinalIgnoreCase);
            }

            if (value is not null && DeviceStatusCatalog.IsValidActiveStatus(value.Status))
            {
                SelectedStatus = value.Status;
            }

            UpdateStatusCommand.RaiseCanExecuteChanged();
            UpdateTransactionDevicesCommand.RaiseCanExecuteChanged();
            OpenPdfCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedResultSummary));
        }
    }

    public string Technician
    {
        get => _technician;
        set => SetProperty(ref _technician, value ?? string.Empty);
    }

    public string SelectedStatus
    {
        get => _selectedStatus;
        set => SetProperty(ref _selectedStatus, value ?? DeviceStatusCatalog.InShop);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value ?? string.Empty);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SearchCommand.RaiseCanExecuteChanged();
                UpdateStatusCommand.RaiseCanExecuteChanged();
                UpdateTransactionDevicesCommand.RaiseCanExecuteChanged();
                OpenPdfCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand UpdateStatusCommand { get; }
    public AsyncRelayCommand UpdateTransactionDevicesCommand { get; }
    public AsyncRelayCommand OpenPdfCommand { get; }

    public async Task SearchAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            if (_recordCodes.TryParse(SearchQuery, out var recordId))
            {
                SearchQuery = recordId;
            }
            await LoadTechnicianSuggestionsAsync();
            await ReloadResultsAsync();
            _status.Message = $"Found {Results.Count} device record(s).";
        }
        catch (Exception ex)
        {
            ShowError("Search equipment", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UpdateStatusAsync()
    {
        if (SelectedResult is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Technician))
        {
            MessageBox.Show(
                "Enter the technician making this status change.",
                "Equipment status",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!DeviceStatusCatalog.IsValidActiveStatus(SelectedStatus))
        {
            MessageBox.Show(
                "Select a valid equipment status.",
                "Equipment status",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var selectedDeviceId = SelectedResult.DeviceId;
        var confirmation = MessageBox.Show(
            $"Change serial number '{SelectedResult.SerialNumber}' to '{SelectedStatus}'?",
            "Confirm status change",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await _database.UpdateDeviceStatusAsync(
                SelectedResult.DeviceId,
                SelectedStatus,
                Technician,
                Notes);

            // Update the visible row immediately, then reload from SQLite as the source of truth.
            SelectedResult.Status = SelectedStatus;

            try
            {
                await _excel.ExportAsync();
            }
            catch (IOException ex)
            {
                _logger.Warning($"Status saved but Excel export failed: {ex.Message}");
            }

            Notes = string.Empty;
            await LoadTechnicianSuggestionsAsync();
            await ReloadResultsAsync(selectedDeviceId);
            _status.Message = $"Device status changed to {SelectedStatus}.";
        }
        catch (Exception ex)
        {
            ShowError("Update equipment status", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UpdateTransactionDevicesAsync()
    {
        if (SelectedResult is null)
        {
            return;
        }

        var selectedDeviceId = SelectedResult.DeviceId;

        try
        {
            IsBusy = true;
            var transaction = await _database.GetTransactionByIdAsync(
                SelectedResult.TransactionId);

            if (transaction is null)
            {
                throw new InvalidOperationException(
                    "The selected 1297 transaction could not be found in the database.");
            }

            if (transaction.IsArchived)
                throw new InvalidOperationException("This 1297 is archived. View its signed documents from Dashboard > Archive.");

            var devices = await _database.GetTransactionDeviceStatusItemsAsync(transaction.Id);
            var technicianSuggestions = await _database.GetTechnicianSuggestionsAsync();

            if (devices.Count == 0)
            {
                MessageBox.Show(
                    "This 1297 does not contain any device records.",
                    "Update devices in 1297",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var transactionItem = new RecentTransactionItem
            {
                TransactionId = transaction.Id,
                TicketNumber = transaction.TicketNumber,
                CustomerName = transaction.Customer.DisplayName,
                Organization = transaction.Organization,
                Technician = transaction.Technician,
                DeviceCount = transaction.Devices.Count,
                StatusSummary = string.Join(
                    ", ",
                    transaction.Devices
                        .Select(device => device.Status)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)),
                IssuedAt = transaction.IssuedAt,
                PdfPath = transaction.PdfPath
            };

            var dialogViewModel = new DeviceStatusDialogViewModel(
                transactionItem,
                devices,
                technicianSuggestions.Select(item => item.NameGrade));
            var dialog = new DeviceStatusDialog
            {
                Owner = Application.Current.MainWindow,
                DataContext = dialogViewModel
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var updates = dialogViewModel.BuildUpdates();
            if (updates.Count == 0)
            {
                MessageBox.Show(
                    "No device statuses were changed.",
                    "Update devices in 1297",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            PartialPickupResult? pickup = null;
            var pickedUpIds = updates.Where(update => update.MarkReturned).Select(update => update.DeviceId).ToArray();
            if (pickedUpIds.Length > 0)
            {
                pickup = await new PartialPickupDialogService(_database, _workflow, _adobe)
                    .CompleteAsync(transaction.Id, pickedUpIds, dialogViewModel.Technician, dialogViewModel.Notes);
                if (pickup is null) return;
            }

            var ordinaryUpdates = updates.Where(update => !update.MarkReturned).ToArray();
            if (ordinaryUpdates.Length > 0)
            {
                try
                {
                    await _database.UpdateDeviceStatusesAsync(ordinaryUpdates,
                        dialogViewModel.Technician, dialogViewModel.Notes);
                }
                catch (Exception ex) when (pickup is not null)
                {
                    throw new InvalidOperationException(
                        "The signed pickup was saved successfully, but the other working-status changes could not be saved. " +
                        "Refresh this record and retry only the ordinary status changes.", ex);
                }
            }

            try
            {
                await _excel.ExportAsync();
            }
            catch (IOException ex)
            {
                _logger.Warning($"Device statuses were saved but Excel export failed: {ex.Message}");
            }

            await LoadTechnicianSuggestionsAsync();
            await ReloadResultsAsync(selectedDeviceId);

            MessageBox.Show(
                pickup is null ? "The selected device updates were saved." : PartialPickupDialogService.CompletionMessage(pickup),
                "Update devices in 1297",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _status.Message = "The 1297 device statuses were updated.";
        }
        catch (SignatureValidationException ex)
        {
            _logger.Error("Partial pickup signature validation failed.", ex);
            DiagnosticDetailsDialog.Show("Pickup signature could not be verified", ex.Message,
                ex.Diagnostics.ToDisplayText());
        }
        catch (Exception ex)
        {
            ShowError("Update devices in 1297", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadResultsAsync(long? selectDeviceId = null)
    {
        var results = await _database.SearchDevicesAsync(SearchQuery);
        Results.Clear();
        foreach (var result in results)
        {
            Results.Add(result);
        }

        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasNoResults));
        OnPropertyChanged(nameof(EmptyResultsMessage));

        SelectedResult = selectDeviceId.HasValue
            ? Results.FirstOrDefault(result => result.DeviceId == selectDeviceId.Value)
            : null;
    }

    private async Task LoadTechnicianSuggestionsAsync()
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

    private async Task OpenPdfAsync()
    {
        if (SelectedResult is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var path = await _documents.CreateCurrentCopyAsync(SelectedResult.TransactionId, forPrinting: false);
            _adobe.OpenPdf(path);
        }
        catch (Exception ex)
        {
            ShowError("Open PDF", ex);
        }
        finally { IsBusy = false; }
    }

    private void ShowError(string title, Exception exception)
    {
        _logger.Error(title, exception);
        _status.Message = $"{title} failed.";

        MessageBox.Show(
            exception.Message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
