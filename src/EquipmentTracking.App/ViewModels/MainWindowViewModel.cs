using System.ComponentModel;
using System.Reflection;
using System.Windows;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Services;

namespace EquipmentTracking.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly DashboardViewModel _dashboard;
    private readonly IntakeViewModel _intake;
    private readonly ReturnsViewModel _returns;
    private readonly RecoveryViewModel _recovery;
    private readonly SettingsViewModel _settings;
    private readonly HowToUseViewModel _howToUse;
    private readonly ExcelExportService _excel;
    private readonly SettingsService _settingsService;
    private readonly ThemeService _themeService;
    private readonly StatusService _status;
    private readonly FileLogger _logger;

    private object _currentViewModel;
    private string _pageTitle = "Dashboard";
    private string _currentSection = "Dashboard";
    private string _selectedTheme;

    public MainWindowViewModel(
        DashboardViewModel dashboard,
        IntakeViewModel intake,
        ReturnsViewModel returns,
        RecoveryViewModel recovery,
        SettingsViewModel settings,
        HowToUseViewModel howToUse,
        ExcelExportService excel,
        SettingsService settingsService,
        ThemeService themeService,
        StatusService status,
        FileLogger logger,
        int pendingRecoveryCount)
    {
        _dashboard = dashboard;
        _intake = intake;
        _returns = returns;
        _recovery = recovery;
        _settings = settings;
        _howToUse = howToUse;
        _excel = excel;
        _settingsService = settingsService;
        _themeService = themeService;
        _status = status;
        _logger = logger;
        _currentViewModel = pendingRecoveryCount > 0 ? recovery : dashboard;
        _pageTitle = pendingRecoveryCount > 0 ? "Recovery" : "Dashboard";
        _currentSection = pendingRecoveryCount > 0 ? "Recovery" : "Dashboard";
        _selectedTheme = ThemeService.Normalize(settingsService.Current.Theme);

        NavigateCommand = new AsyncRelayCommand(NavigateAsync);
        ExportExcelCommand = new AsyncRelayCommand(ExportExcelAsync);

        _status.PropertyChanged += StatusOnPropertyChanged;
        if (pendingRecoveryCount > 0)
        {
            _ = _recovery.RefreshAsync();
        }
        else
        {
            _ = _dashboard.RefreshAsync();
        }
    }

    public object CurrentViewModel
    {
        get => _currentViewModel;
        private set => SetProperty(ref _currentViewModel, value);
    }

    public string PageTitle
    {
        get => _pageTitle;
        private set => SetProperty(ref _pageTitle, value);
    }

    public string CurrentSection
    {
        get => _currentSection;
        private set
        {
            if (!SetProperty(ref _currentSection, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsDashboardSelected));
            OnPropertyChanged(nameof(IsIntakeSelected));
            OnPropertyChanged(nameof(IsReturnsSelected));
            OnPropertyChanged(nameof(IsRecoverySelected));
            OnPropertyChanged(nameof(IsHowToUseSelected));
            OnPropertyChanged(nameof(IsSettingsSelected));
        }
    }

    public bool IsDashboardSelected => IsCurrentSection("Dashboard");
    public bool IsIntakeSelected => IsCurrentSection("Intake");
    public bool IsReturnsSelected => IsCurrentSection("Returns");
    public bool IsRecoverySelected => IsCurrentSection("Recovery");
    public bool IsHowToUseSelected => IsCurrentSection("HowToUse");
    public bool IsSettingsSelected => IsCurrentSection("Settings");

    public IReadOnlyList<string> AvailableThemes => _themeService.AvailableThemes;

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            var normalized = ThemeService.Normalize(value);
            if (!SetProperty(ref _selectedTheme, normalized))
            {
                return;
            }

            _themeService.Apply(normalized);
            _ = SaveThemeAsync(normalized);
        }
    }

    public string StatusMessage => _status.Message;

    public string AppVersion
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            return string.IsNullOrWhiteSpace(informationalVersion)
                ? assembly.GetName().Version?.ToString(3) ?? "0.9.5-alpha.1"
                : informationalVersion.Split('+')[0];
        }
    }

    public AsyncRelayCommand NavigateCommand { get; }
    public AsyncRelayCommand ExportExcelCommand { get; }

    private async Task NavigateAsync(object? parameter)
    {
        var destination = parameter?.ToString() ?? "Dashboard";

        switch (destination)
        {
            case "Intake":
                CurrentSection = "Intake";
                CurrentViewModel = _intake;
                PageTitle = "New intake";
                break;

            case "Returns":
                CurrentSection = "Returns";
                CurrentViewModel = _returns;
                PageTitle = "Equipment status";
                await _returns.SearchAsync();
                break;

            case "Recovery":
                CurrentSection = "Recovery";
                CurrentViewModel = _recovery;
                PageTitle = "Recovery";
                await _recovery.RefreshAsync();
                break;

            case "Settings":
                CurrentSection = "Settings";
                CurrentViewModel = _settings;
                PageTitle = "Settings";
                _settings.Reload();
                break;

            case "HowToUse":
                CurrentSection = "HowToUse";
                CurrentViewModel = _howToUse;
                PageTitle = "How to use";
                break;

            default:
                CurrentSection = "Dashboard";
                CurrentViewModel = _dashboard;
                PageTitle = "Dashboard";
                await _dashboard.RefreshAsync();
                break;
        }
    }

    private bool IsCurrentSection(string section) =>
        string.Equals(CurrentSection, section, StringComparison.Ordinal);

    private async Task SaveThemeAsync(string theme)
    {
        try
        {
            var updated = _settingsService.Current.Clone();
            updated.Theme = theme;
            await _settingsService.SaveAsync(updated);
            _status.Message = $"{theme} theme applied.";
        }
        catch (Exception ex)
        {
            _logger.Warning($"Theme preference could not be saved: {ex.Message}");
            _status.Message = "Theme applied for this session, but the preference could not be saved.";
        }
    }

    private async Task ExportExcelAsync()
    {
        try
        {
            _status.Message = "Exporting Excel workbook...";
            var path = await _excel.ExportAsync();
            _status.Message = $"Excel export updated: {path}";
        }
        catch (Exception ex)
        {
            _logger.Error("Manual Excel export failed.", ex);
            _status.Message = "Excel export failed.";
            MessageBox.Show(
                ex.Message,
                "Excel export",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void StatusOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StatusService.Message))
        {
            OnPropertyChanged(nameof(StatusMessage));
        }
    }
}
