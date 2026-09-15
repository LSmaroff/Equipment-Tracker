using System.Windows;

namespace EquipmentTracking.App.Infrastructure;

public sealed class StatusService : ObservableObject
{
    private string _message = "Ready";

    public string Message
    {
        get => _message;
        set
        {
            var normalized = value ?? string.Empty;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => Message = normalized));
                return;
            }

            SetProperty(ref _message, normalized);
        }
    }
}
