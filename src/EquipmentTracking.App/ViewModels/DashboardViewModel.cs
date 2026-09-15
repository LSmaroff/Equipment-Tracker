using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;
using EquipmentTracking.App.Views;

namespace EquipmentTracking.App.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private static readonly string[] ChartPalette =
    [
        "#4E79A7", "#F28E2B", "#59A14F", "#E15759", "#76B7B2",
        "#B07AA1", "#EDC948", "#9C755F", "#FF9DA7", "#7F8C8D"
    ];

    private readonly DatabaseService _database;
    private readonly TransactionWorkflowService _workflow;
    private readonly AdobeService _adobe;
    private readonly PrintJobService _printJobs;
    private readonly RecordCodeService _recordCodes;
    private readonly StatusService _status;
    private readonly FileLogger _logger;
    private int _activeDeviceCount;
    private int _returnedTodayCount;
    private int _activeTransactionCount;
    private int _archivedTransactionCount;
    private RecentTransactionItem? _selectedTransaction;
    private string _searchQuery = string.Empty;
    private string _selectedChartMetric = "Status";
    private string _selectedTransactionScope = "Active 1297s";
    private bool _isBusy;

    public DashboardViewModel(
        DatabaseService database,
        TransactionWorkflowService workflow,
        AdobeService adobe,
        PrintJobService printJobs,
        RecordCodeService recordCodes,
        StatusService status,
        FileLogger logger)
    {
        _database = database;
        _workflow = workflow;
        _adobe = adobe;
        _printJobs = printJobs;
        _recordCodes = recordCodes;
        _status = status;
        _logger = logger;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy);
        OpenSelectedPdfCommand = new RelayCommand(OpenSelectedPdf, () => SelectedTransaction is not null);
        PrintTwoCopiesCommand = new AsyncRelayCommand(
            PrintTwoCopiesAsync,
            () => !IsBusy && SelectedTransaction is not null);
        CloseoutSelectedCommand = new AsyncRelayCommand(
            CloseoutSelectedAsync,
            () => !IsBusy && !IsArchiveView);
        PartialPickupCommand = new AsyncRelayCommand(PartialPickupSelectedAsync,
            () => !IsBusy && !IsArchiveView && SelectedTransaction is { IsArchived: false });
        DocumentsCommand = new AsyncRelayCommand(ShowDocumentsAsync,
            () => !IsBusy && SelectedTransaction is not null);
    }

    public IReadOnlyList<string> TransactionScopes { get; } = ["Active 1297s", "Archive"];
    public IReadOnlyList<string> ChartMetrics { get; } = ["Model", "Customer", "Status"];
    public ObservableCollection<RecentTransactionItem> Transactions { get; } = [];
    public ObservableCollection<PieChartSlice> ChartSlices { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SearchCommand { get; }
    public RelayCommand OpenSelectedPdfCommand { get; }
    public AsyncRelayCommand PrintTwoCopiesCommand { get; }
    public AsyncRelayCommand CloseoutSelectedCommand { get; }
    public AsyncRelayCommand PartialPickupCommand { get; }
    public AsyncRelayCommand DocumentsCommand { get; }

    public int ActiveDeviceCount { get => _activeDeviceCount; private set => SetProperty(ref _activeDeviceCount, value); }
    public int ReturnedTodayCount { get => _returnedTodayCount; private set => SetProperty(ref _returnedTodayCount, value); }
    public int ActiveTransactionCount { get => _activeTransactionCount; private set => SetProperty(ref _activeTransactionCount, value); }
    public int ArchivedTransactionCount { get => _archivedTransactionCount; private set => SetProperty(ref _archivedTransactionCount, value); }

    public bool HasTransactions => Transactions.Count > 0;
    public bool HasNoTransactions => !HasTransactions;
    public bool HasChartData => ChartSlices.Any(slice => slice.Value > 0);
    public bool HasNoChartData => !HasChartData;

    public string SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value ?? string.Empty);
    }

    public string SelectedTransactionScope
    {
        get => _selectedTransactionScope;
        set
        {
            if (SetProperty(ref _selectedTransactionScope, value ?? "Active 1297s"))
            {
                OnPropertyChanged(nameof(IsArchiveView));
                OnPropertyChanged(nameof(CloseoutAvailabilityMessage));
                CloseoutSelectedCommand.RaiseCanExecuteChanged();
                PartialPickupCommand.RaiseCanExecuteChanged();
                _ = LoadTransactionsAsync();
            }
        }
    }

    public bool IsArchiveView => string.Equals(SelectedTransactionScope, "Archive", StringComparison.OrdinalIgnoreCase);

    public string CloseoutAvailabilityMessage
    {
        get
        {
            if (IsBusy)
            {
                return "Please wait for the current dashboard operation to finish.";
            }

            if (IsArchiveView)
            {
                return "Archived 1297s are already closed and cannot be closed out again.";
            }

            if (SelectedTransaction is null)
            {
                return "Select an active 1297 row for a pickup or full closeout. Documents are available for active and archived records.";
            }

            if (SelectedTransaction.IsArchived)
            {
                return "The selected 1297 is already archived.";
            }

            return $"Ticket {SelectedTransaction.TicketNumber}: choose Partial pickup to collect selected devices with a signed copy.";
        }
    }

    public string PrintAvailabilityMessage
    {
        get
        {
            if (IsBusy)
            {
                return "Please wait for the current dashboard operation to finish.";
            }

            return SelectedTransaction is null
                ? "Select an active or archived 1297 row to print two copies on one US Letter sheet."
                : $"Print two copies of ticket {SelectedTransaction.TicketNumber} on one US Letter sheet.";
        }
    }

    public string SelectedChartMetric
    {
        get => _selectedChartMetric;
        set
        {
            if (SetProperty(ref _selectedChartMetric, value ?? "Status"))
            {
                _ = RefreshChartAsync();
            }
        }
    }

    public RecentTransactionItem? SelectedTransaction
    {
        get => _selectedTransaction;
        set
        {
            if (SetProperty(ref _selectedTransaction, value))
            {
                OpenSelectedPdfCommand.RaiseCanExecuteChanged();
                PrintTwoCopiesCommand.RaiseCanExecuteChanged();
                CloseoutSelectedCommand.RaiseCanExecuteChanged();
                PartialPickupCommand.RaiseCanExecuteChanged();
                DocumentsCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CloseoutAvailabilityMessage));
                OnPropertyChanged(nameof(PrintAvailabilityMessage));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
                SearchCommand.RaiseCanExecuteChanged();
                PrintTwoCopiesCommand.RaiseCanExecuteChanged();
                CloseoutSelectedCommand.RaiseCanExecuteChanged();
                PartialPickupCommand.RaiseCanExecuteChanged();
                DocumentsCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CloseoutAvailabilityMessage));
                OnPropertyChanged(nameof(PrintAvailabilityMessage));
            }
        }
    }

    private async Task PartialPickupSelectedAsync()
    {
        if (SelectedTransaction is null || SelectedTransaction.IsArchived) return;
        try
        {
            IsBusy = true;
            var result = await new PartialPickupDialogService(_database, _workflow, _adobe)
                .SelectAndCompleteAsync(SelectedTransaction.TransactionId);
            if (result is null) return;
            IsBusy = false;
            await RefreshAsync();
            var message = PartialPickupDialogService.CompletionMessage(result);
            _status.Message = message.Replace("\n\n", " ", StringComparison.Ordinal);
            MessageBox.Show(message, "Pickup completed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (SignatureValidationException ex)
        {
            _logger.Error("Partial pickup signature validation failed.", ex);
            DiagnosticDetailsDialog.Show("Pickup signature could not be verified", ex.Message,
                ex.Diagnostics.ToDisplayText());
        }
        catch (Exception ex) { ShowError("Partial pickup", ex); }
        finally { IsBusy = false; }
    }

    private async Task ShowDocumentsAsync()
    {
        if (SelectedTransaction is null) return;
        try
        {
            IsBusy = true;
            var transaction = await _database.GetTransactionByIdAsync(SelectedTransaction.TransactionId)
                ?? throw new InvalidOperationException("The selected 1297 could not be found.");
            var artifacts = await _database.GetFileArtifactsAsync(transaction.Id);
            var pickups = await _database.GetPickupReceiptsAsync(transaction.Id);
            var devices = await _database.GetTransactionDeviceStatusItemsAsync(transaction.Id);
            new TransactionDocumentsDialog
            {
                Owner = Application.Current.MainWindow,
                DataContext = new TransactionDocumentsDialogViewModel(transaction, artifacts, pickups, devices, _adobe)
            }.ShowDialog();
        }
        catch (Exception ex) { ShowError("1297 documents", ex); }
        finally { IsBusy = false; }
    }

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            var summary = await _database.GetDashboardSummaryAsync();
            ActiveDeviceCount = summary.ActiveDeviceCount;
            ReturnedTodayCount = summary.ReturnedTodayCount;
            ActiveTransactionCount = summary.ActiveTransactionCount;
            ArchivedTransactionCount = summary.ArchivedTransactionCount;
            await LoadTransactionsAsync();
            await RefreshChartAsync();
            _status.Message = "Dashboard refreshed.";
        }
        catch (Exception ex) { ShowError("Dashboard refresh", ex); }
        finally { IsBusy = false; }
    }

    public async Task SearchAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            if (_recordCodes.TryParse(SearchQuery, out var recordId))
            {
                SearchQuery = recordId;
                var exactTransaction = await _database.GetTransactionByIdAsync(recordId);
                if (exactTransaction is not null)
                {
                    var requiredScope = exactTransaction.IsArchived ? "Archive" : "Active 1297s";
                    if (!string.Equals(
                            _selectedTransactionScope,
                            requiredScope,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        _selectedTransactionScope = requiredScope;
                        OnPropertyChanged(nameof(SelectedTransactionScope));
                        OnPropertyChanged(nameof(IsArchiveView));
                        OnPropertyChanged(nameof(CloseoutAvailabilityMessage));
                        CloseoutSelectedCommand.RaiseCanExecuteChanged();
                    }
                }
            }
            await LoadTransactionsAsync();
            if (_recordCodes.TryParse(SearchQuery, out var selectedRecordId))
            {
                SelectedTransaction = Transactions.FirstOrDefault(item =>
                    string.Equals(
                        item.TransactionId,
                        selectedRecordId,
                        StringComparison.OrdinalIgnoreCase));
            }
            _status.Message = $"Found {Transactions.Count} matching 1297 transaction(s).";
        }
        catch (Exception ex) { ShowError("Search 1297s", ex); }
        finally { IsBusy = false; }
    }

    private async Task LoadTransactionsAsync()
    {
        var selectedTransactionId = SelectedTransaction?.TransactionId;
        var results = await _database.SearchTransactionsAsync(SearchQuery, IsArchiveView);
        Transactions.Clear();
        foreach (var item in results) Transactions.Add(item);

        OnPropertyChanged(nameof(HasTransactions));
        OnPropertyChanged(nameof(HasNoTransactions));

        SelectedTransaction = string.IsNullOrWhiteSpace(selectedTransactionId)
            ? null
            : Transactions.FirstOrDefault(item =>
                string.Equals(item.TransactionId, selectedTransactionId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task RefreshChartAsync()
    {
        try
        {
            var points = await _database.GetDeviceChartDataAsync(SelectedChartMetric);
            var total = points.Sum(point => point.Value);
            ChartSlices.Clear();
            for (var index = 0; index < points.Count; index++)
            {
                var point = points[index];
                ChartSlices.Add(new PieChartSlice
                {
                    Label = point.Label,
                    Value = point.Value,
                    Percentage = total == 0 ? 0 : point.Value * 100d / total,
                    Brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ChartPalette[index % ChartPalette.Length]))
                });
            }

            OnPropertyChanged(nameof(HasChartData));
            OnPropertyChanged(nameof(HasNoChartData));
        }
        catch (Exception ex)
        {
            ChartSlices.Clear();
            OnPropertyChanged(nameof(HasChartData));
            OnPropertyChanged(nameof(HasNoChartData));
            _logger.Warning($"Dashboard chart refresh failed: {ex.GetType().Name}.");
        }
    }

    private void OpenSelectedPdf()
    {
        if (SelectedTransaction is null) return;
        try { _adobe.OpenPdf(SelectedTransaction.PdfPath); }
        catch (Exception ex) { ShowError("Open PDF", ex); }
    }

    private async Task PrintTwoCopiesAsync()
    {
        if (SelectedTransaction is null)
        {
            MessageBox.Show(
                "Select an active or archived 1297 row before printing.",
                "Select a 1297",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        string? printPath = null;
        string? printedTicketNumber = null;
        var printingPromptCompleted = false;
        try
        {
            IsBusy = true;
            var selected = SelectedTransaction;
            printedTicketNumber = selected.TicketNumber;
            printPath = await Task.Run(() => _printJobs.CreateTwoCopyLetterSheet(
                selected.PdfPath,
                selected.TicketNumber));
            var printDialogRequested = _adobe.OpenPdfForPrinting(printPath);
            _status.Message = printDialogRequested
                ? $"1297 print dialog opened for ticket {selected.TicketNumber}."
                : $"1297 print sheet opened for ticket {selected.TicketNumber}.";

            var viewerInstructions = printDialogRequested
                ? "The print dialog is open for the temporary 1297 sheet. "
                : "The temporary 1297 sheet is open in the default PDF application. " +
                  "Press Ctrl+P or use that application's Print command. ";
            MessageBox.Show(
                viewerInstructions +
                "Keep this message open until printing is finished. " +
                "Use US Letter, portrait, one-sided printing, and a printer copy count of 1. " +
                "When you close this message, the temporary print PDF will be deleted.",
                "1297 print sheet opened",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            printingPromptCompleted = true;
        }
        catch (Exception ex)
        {
            ShowError("Print 1297's", ex);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(printPath))
            {
                try
                {
                    var deleted = await _printJobs.DeleteTemporaryPrintJobAsync(printPath);
                    if (printingPromptCompleted && !string.IsNullOrWhiteSpace(printedTicketNumber))
                    {
                        _status.Message = deleted
                            ? $"Printing finished and the temporary copy for ticket {printedTicketNumber} was deleted."
                            : $"Printing finished. The temporary copy for ticket {printedTicketNumber} will be deleted when the PDF viewer releases it.";
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(
                        $"Temporary print-job cleanup failed after printing: {ex.GetType().Name}.");
                }
            }

            IsBusy = false;
        }
    }

    private async Task CloseoutSelectedAsync()
    {
        if (SelectedTransaction is null)
        {
            MessageBox.Show(
                "Select an active 1297 row before starting closeout.",
                "Select a 1297",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (SelectedTransaction.IsArchived || IsArchiveView)
        {
            MessageBox.Show(
                "Archived 1297s are already closed and cannot be closed out again.",
                "1297 already archived",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            IsBusy = true;
            var transaction = await _database.GetTransactionByIdAsync(SelectedTransaction.TransactionId)
                ?? throw new InvalidOperationException("The selected transaction could not be found.");
            var suggestions = await _database.GetTechnicianSuggestionsAsync();

            var preparation = await _workflow.BeginCloseoutAsync(
                transaction,
                DateTimeOffset.Now);
            _adobe.OpenPdf(transaction.PdfPath);

            var dialogViewModel = new CloseoutDialogViewModel(
                transaction,
                suggestions.Select(item => item.NameGrade),
                _adobe);
            var dialog = new CloseoutDialog
            {
                Owner = Application.Current.MainWindow,
                DataContext = dialogViewModel
            };
            IsBusy = false;
            if (dialog.ShowDialog() != true)
            {
                _status.Message = "Closeout remains available in Recovery until it is resumed or rolled back.";
                return;
            }

            IsBusy = true;
            await _workflow.PrepareCloseoutAsync(transaction, dialogViewModel.Technician);
            var result = await _workflow.FinalizeCloseoutAsync(new CloseoutRequest
            {
                Transaction = transaction,
                Technician = dialogViewModel.Technician,
                ClosedAt = DateTimeOffset.Now,
                SignatureSearchStartedAt = preparation.SignatureSearchStartedAt,
                ExistingSignatureFingerprints = preparation.ExistingSignatureFingerprints
            });
            IsBusy = false;
            await RefreshAsync();
            MessageBox.Show(
                $"1297 {result.Transaction.TicketNumber} was closed and moved to:\n\n{result.ArchivedPdfPath}",
                "1297 archived",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (SignatureValidationException ex)
        {
            _logger.Error("Closeout signature validation failed.", ex);
            _status.Message = "Pickup Signature could not be verified. Review the diagnostic details.";
            DiagnosticDetailsDialog.Show(
                "Pickup Signature could not be verified",
                ex.Message,
                ex.Diagnostics.ToDisplayText());
        }
        catch (Exception ex)
        {
            ShowError("Close out 1297", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ShowError(string title, Exception exception)
    {
        _logger.Error(title, exception);
        _status.Message = $"{title} failed.";
        MessageBox.Show(exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
