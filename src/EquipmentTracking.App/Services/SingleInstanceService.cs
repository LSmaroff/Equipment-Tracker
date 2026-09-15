using System.Security.Cryptography;
using System.Text;
using System.Security.Principal;
using System.Threading;
using System.Windows;

namespace EquipmentTracking.App.Services;

public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listenerTask;
    private bool _ownsMutex;

    public SingleInstanceService()
    {
        var userIdentity = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(userIdentity))
        {
            userIdentity = $"{Environment.UserDomainName}\\{Environment.UserName}";
        }

        var userScope = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(userIdentity)))[..16];
        var baseName = $"58SOW.EquipmentTrackingPlatform.{userScope}";

        _mutex = new Mutex(initiallyOwned: true, $"Local\\{baseName}.Mutex", out var createdNew);
        _ownsMutex = createdNew;
        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            $"Local\\{baseName}.Activate");
    }

    public bool IsPrimaryInstance => _ownsMutex;

    public void SignalPrimaryInstance()
    {
        try
        {
            _activationEvent.Set();
        }
        catch
        {
            // A secondary launch must be able to exit even if signaling fails.
        }
    }

    public void StartActivationListener(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (!IsPrimaryInstance || _listenerTask is not null)
        {
            return;
        }

        _listenerTask = Task.Run(() =>
        {
            var waitHandles = new WaitHandle[] { _activationEvent, _cancellation.Token.WaitHandle };
            while (!_cancellation.IsCancellationRequested)
            {
                var signaled = WaitHandle.WaitAny(waitHandles);
                if (signaled != 0)
                {
                    return;
                }

                application.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var window = application.MainWindow;
                    if (window is null)
                    {
                        return;
                    }

                    if (window.WindowState == WindowState.Minimized)
                    {
                        window.WindowState = WindowState.Normal;
                    }

                    window.Show();
                    window.Activate();
                    window.Topmost = true;
                    window.Topmost = false;
                    window.Focus();
                }));
            }
        });
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try
        {
            // Wake the listener before disposing its wait handles. This prevents a
            // shutdown race from surfacing as an unobserved ObjectDisposedException.
            _activationEvent.Set();
            _listenerTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Shutdown must continue even if the listener has already stopped.
        }
        catch (ObjectDisposedException)
        {
            // The event may already be disposed during an unusual shutdown path.
        }

        _activationEvent.Dispose();
        _cancellation.Dispose();

        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex may already have been released during shutdown.
            }

            _ownsMutex = false;
        }

        _mutex.Dispose();
    }
}
