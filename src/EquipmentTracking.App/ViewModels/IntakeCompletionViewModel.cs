using EquipmentTracking.App.Infrastructure;

namespace EquipmentTracking.App.ViewModels;

public sealed class IntakeCompletionViewModel : ObservableObject
{
    private string _transactionId = string.Empty;
    private string _ticketNumber = string.Empty;

    public IntakeCompletionViewModel(Func<bool> isBusy, Func<string, Task> print)
    {
        PrintCommand = new AsyncRelayCommand(() => print(_transactionId),
            () => HasCompletedIntake && !isBusy());
    }

    public bool HasCompletedIntake => _transactionId.Length > 0;
    public string Summary => $"Intake completed · Ticket {_ticketNumber}";
    public AsyncRelayCommand PrintCommand { get; }

    // Called only after successful finalization, never for a prepared/unsigned form.
    public void RecordCompletion(string transactionId, string ticketNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        _transactionId = transactionId;
        _ticketNumber = ticketNumber;
        NotifyChanged();
    }

    public void Clear()
    {
        _transactionId = string.Empty;
        _ticketNumber = string.Empty;
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(HasCompletedIntake));
        OnPropertyChanged(nameof(Summary));
        PrintCommand.RaiseCanExecuteChanged();
    }
}
