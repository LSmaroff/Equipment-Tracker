using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;
using EquipmentTracking.App.Views;

namespace EquipmentTracking.App.ViewModels;

public sealed class RecoveryViewModel : ObservableObject
{
    private readonly WorkflowJournalService _journals;
    private readonly TransactionWorkflowService _workflow;
    private readonly AdobeService _adobe;
    private readonly StatusService _status;
    private readonly FileLogger _logger;
    private WorkflowJournalEntry? _selectedItem;
    private bool _isBusy;
    private string _message = "Interrupted intake, pickup, and closeout operations appear here.";

    public RecoveryViewModel(
        WorkflowJournalService journals,
        TransactionWorkflowService workflow,
        AdobeService adobe,
        StatusService status,
        FileLogger logger)
    {
        _journals = journals;
        _workflow = workflow;
        _adobe = adobe;
        _status = status;
        _logger = logger;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        ResumeCommand = new AsyncRelayCommand(ResumeSelectedAsync, () => !IsBusy && SelectedItem is not null);
        RollbackCommand = new AsyncRelayCommand(RollbackSelectedAsync, () => !IsBusy && SelectedItem is not null);
        OpenPdfCommand = new RelayCommand(OpenSelectedPdf, () => SelectedItem is not null && HasExistingPdf(SelectedItem));
    }

    public ObservableCollection<WorkflowJournalEntry> Items { get; } = [];

    public bool HasItems => Items.Count > 0;
    public bool HasNoItems => !HasItems;
    public bool HasSelection => SelectedItem is not null;

    public WorkflowJournalEntry? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                ResumeCommand.RaiseCanExecuteChanged();
                RollbackCommand.RaiseCanExecuteChanged();
                OpenPdfCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(SelectedDetails));
                OnPropertyChanged(nameof(HasSelection));
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
                ResumeCommand.RaiseCanExecuteChanged();
                RollbackCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public string SelectedDetails => SelectedItem is null
        ? "Select an item to view its recovery stage and most recent error."
        : $"Operation: {SelectedItem.OperationType}\n" +
          $"Transaction: {SelectedItem.TransactionId}\n" +
          $"Stage: {SelectedItem.Stage}\n" +
          $"Status: {SelectedItem.Status}\n" +
          $"Last updated: {SelectedItem.UpdatedAt.LocalDateTime:g}\n" +
          (string.IsNullOrWhiteSpace(SelectedItem.LastError)
              ? "No error details were recorded."
              : $"\nLast error:\n{SelectedItem.LastError}");

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ResumeCommand { get; }
    public AsyncRelayCommand RollbackCommand { get; }
    public RelayCommand OpenPdfCommand { get; }

    public async Task RefreshAsync()
    {
        try
        {
            IsBusy = true;
            var selectedId = SelectedItem?.OperationId;
            var pending = await _journals.GetPendingAsync();
            Items.Clear();
            foreach (var item in pending)
            {
                Items.Add(item);
            }

            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(HasNoItems));

            SelectedItem = Items.FirstOrDefault(item =>
                string.Equals(item.OperationId, selectedId, StringComparison.OrdinalIgnoreCase));
            Message = Items.Count == 0
                ? "No interrupted workflows require attention."
                : $"{Items.Count} interrupted workflow(s) can be resumed or rolled back.";
            _status.Message = Message;
        }
        catch (Exception ex)
        {
            ShowError("Refresh recovery items", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ResumeSelectedAsync()
    {
        var item = SelectedItem;
        if (item is null)
        {
            return;
        }

        var response = MessageBox.Show(
            $"Resume {item.DisplayName} from stage '{item.Stage}'?\n\n" +
            "Make sure Adobe is closed and any required signature has already been saved.",
            "Resume interrupted workflow",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (response != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var result = await _workflow.ResumeWorkflowAsync(item);
            Message = result.Message;
            _status.Message = result.Message;
            await RefreshAsync();

            MessageBox.Show(
                result.Message + (string.IsNullOrWhiteSpace(result.PdfPath)
                    ? string.Empty
                    : $"\n\nPDF:\n{result.PdfPath}"),
                "Workflow resumed",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (SignatureValidationException ex)
        {
            _logger.Error("Resume workflow signature validation failed.", ex);
            DiagnosticDetailsDialog.Show(
                "Signature could not be verified",
                ex.Message,
                ex.Diagnostics.ToDisplayText());
        }
        catch (Exception ex)
        {
            ShowError("Resume interrupted workflow", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RollbackSelectedAsync()
    {
        var item = SelectedItem;
        if (item is null)
        {
            return;
        }

        var response = MessageBox.Show(
            $"Roll back {item.DisplayName}?\n\n" +
            "The program will preserve committed database records and will refuse unsafe rollbacks. " +
            "Uncommitted temporary files may be removed.",
            "Roll back interrupted workflow",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (response != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await _workflow.RollbackWorkflowAsync(item);
            Message = $"{item.DisplayName} was rolled back safely.";
            _status.Message = Message;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowError("Roll back interrupted workflow", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenSelectedPdf()
    {
        var item = SelectedItem;
        if (item is null)
        {
            return;
        }

        var path = FirstExistingPath(
            item.DestinationPdfPath,
            item.SourcePdfPath,
            item.OriginalSignedPdfPath,
            item.Intake?.Working.PreparedPdfPath);
        if (string.IsNullOrWhiteSpace(path))
        {
            MessageBox.Show(
                "No existing PDF could be found for this recovery item.",
                "Recovery",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            _adobe.OpenPdf(path);
        }
        catch (Exception ex)
        {
            ShowError("Open recovery PDF", ex);
        }
    }

    private static bool HasExistingPdf(WorkflowJournalEntry entry) =>
        !string.IsNullOrWhiteSpace(FirstExistingPath(
            entry.DestinationPdfPath,
            entry.SourcePdfPath,
            entry.OriginalSignedPdfPath,
            entry.Intake?.Working.PreparedPdfPath));

    private static string FirstExistingPath(params string?[] paths) =>
        paths.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)) ?? string.Empty;

    private void ShowError(string title, Exception exception)
    {
        _logger.Error(title, exception);
        Message = exception.Message;
        _status.Message = $"{title} failed.";
        MessageBox.Show(
            exception.Message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
