using System.Windows;
using System.Windows.Threading;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Services;
using EquipmentTracking.App.Views;

namespace EquipmentTracking.App;

public partial class App : Application, IDisposable
{
    private AppServices? _services;
    private SingleInstanceService? _singleInstance;
    private bool _disposed;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.IsPrimaryInstance)
        {
            _singleInstance.SignalPrimaryInstance();
            Shutdown(0);
            return;
        }

        try
        {
            _services = new AppServices();
            await _services.InitializeAsync();

            var report = _services.StartupPreflightReport;
            if (report is not null && report.HasWarningsOrFailures)
            {
                var preflightDialog = new PreflightDialog(report);
                var continueStartup = preflightDialog.ShowDialog() == true;
                if (!continueStartup)
                {
                    Shutdown(report.HasBlockingFailures ? 2 : 0);
                    return;
                }
            }

            var window = new MainWindow
            {
                DataContext = _services.CreateMainWindowViewModel()
            };

            MainWindow = window;
            ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
            window.Show();
            _singleInstance.StartActivationListener(this);
        }
        catch (Exception ex)
        {
            _services?.Logger.Error("Application startup failed.", ex);

            MessageBox.Show(
                $"The application could not start.\n\n{ex.Message}",
                "Equipment Tracking Platform",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _services?.Dispose();
        _services = null;
        _singleInstance?.Dispose();
        _singleInstance = null;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        _services?.Logger.Error("Unhandled UI exception.", e.Exception);

        MessageBox.Show(
            $"An unexpected error occurred.\n\n{e.Exception.Message}\n\n" +
            "The error was recorded in the application log. Check Recovery before repeating an interrupted transaction.",
            "Equipment Tracking Platform",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _services?.Logger.Error("Unhandled application exception.", exception);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _services?.Logger.Error("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }
}
