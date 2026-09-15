using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using EquipmentTracking.App.Infrastructure;
using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;
using EquipmentTracking.App.Views;
using Microsoft.Win32;

namespace EquipmentTracking.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly PdfFormService _pdfForms;
    private readonly AdobeService _adobe;
    private readonly AppPaths _paths;
    private readonly BackupService _backups;
    private readonly ScheduledBackupService _scheduledBackups;
    private readonly DatabaseService _database;
    private readonly SupportPackageService _supportPackages;
    private readonly PreflightService _preflight;
    private readonly MaintenanceService _maintenance;
    private readonly WorkflowJournalService _journals;
    private readonly UpdateService _updates;
    private readonly StatusService _status;
    private readonly FileLogger _logger;

    private string _templatePdfPath = string.Empty;
    private string _completedPdfFolder = string.Empty;
    private string _excelExportPath = string.Empty;
    private string _adobeExecutablePath = string.Empty;
    private string _organizationsText = string.Empty;
    private int _maximumDevicesPerForm = 10;
    private bool _requireSignatureForFinalization = true;
    private int _logRetentionDays = 30;
    private int _temporaryFileRetentionDays = 7;
    private int _completedWorkflowRetentionDays = 30;
    private int _backupRetentionCount = 5;
    private bool _scheduledBackupsEnabled = true;
    private string _scheduledBackupFolder = string.Empty;
    private bool _createAutomaticPreMigrationBackups = true;
    private bool _allowTestDataReset;
    private bool _isBusy;

    public SettingsViewModel(
        SettingsService settingsService,
        PdfFormService pdfForms,
        AdobeService adobe,
        AppPaths paths,
        BackupService backups,
        ScheduledBackupService scheduledBackups,
        DatabaseService database,
        SupportPackageService supportPackages,
        PreflightService preflight,
        MaintenanceService maintenance,
        WorkflowJournalService journals,
        UpdateService updates,
        StatusService status,
        FileLogger logger)
    {
        _settingsService = settingsService;
        _pdfForms = pdfForms;
        _adobe = adobe;
        _paths = paths;
        _backups = backups;
        _scheduledBackups = scheduledBackups;
        _database = database;
        _supportPackages = supportPackages;
        _preflight = preflight;
        _maintenance = maintenance;
        _journals = journals;
        _updates = updates;
        _status = status;
        _logger = logger;

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        BrowseTemplateCommand = new RelayCommand(BrowseTemplate);
        BrowseCompletedFolderCommand = new RelayCommand(BrowseCompletedFolder);
        BrowseExcelCommand = new RelayCommand(BrowseExcel);
        BrowseAdobeCommand = new RelayCommand(BrowseAdobe);
        BrowseScheduledBackupFolderCommand = new RelayCommand(BrowseScheduledBackupFolder);
        InspectFieldsCommand = new AsyncRelayCommand(InspectFieldsAsync, () => !IsBusy);
        OpenDataFolderCommand = new RelayCommand(() => _adobe.OpenFolder(_paths.BaseDataDirectory));
        OpenBackupFolderCommand = new RelayCommand(OpenBackupFolder);
        OpenDiagnosticsFolderCommand = new RelayCommand(() => _adobe.OpenFolder(_paths.DiagnosticsDirectory));
        CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync, () => !IsBusy);
        RestoreBackupCommand = new AsyncRelayCommand(StageRestoreAsync, () => !IsBusy);
        CreateSupportPackageCommand = new AsyncRelayCommand(CreateSupportPackageAsync, () => !IsBusy);
        RunPreflightCommand = new AsyncRelayCommand(RunPreflightAsync, () => !IsBusy);
        InstallUpdateCommand = new AsyncRelayCommand(InstallUpdateAsync, () => !IsBusy);
        ResetTestDataCommand = new AsyncRelayCommand(
            ResetTestDataAsync,
            () => !IsBusy && AllowTestDataReset);
        _scheduledBackups.StatusChanged += ScheduledBackups_OnStatusChanged;

        Reload();
    }

    public ObservableCollection<FieldMappingItem> FieldMappings { get; } = [];

    public string TemplatePdfPath { get => _templatePdfPath; set => SetProperty(ref _templatePdfPath, value ?? string.Empty); }
    public string CompletedPdfFolder { get => _completedPdfFolder; set => SetProperty(ref _completedPdfFolder, value ?? string.Empty); }
    public string ExcelExportPath { get => _excelExportPath; set => SetProperty(ref _excelExportPath, value ?? string.Empty); }
    public string AdobeExecutablePath { get => _adobeExecutablePath; set => SetProperty(ref _adobeExecutablePath, value ?? string.Empty); }
    public string ScheduledBackupFolder { get => _scheduledBackupFolder; set => SetProperty(ref _scheduledBackupFolder, value ?? string.Empty); }
    public string OrganizationsText { get => _organizationsText; set => SetProperty(ref _organizationsText, value ?? string.Empty); }

    public int MaximumDevicesPerForm
    {
        get => _maximumDevicesPerForm;
        set => SetProperty(ref _maximumDevicesPerForm, Math.Clamp(value, 1, 10));
    }

    public bool RequireSignatureForFinalization
    {
        get => _requireSignatureForFinalization;
        set => SetProperty(ref _requireSignatureForFinalization, value);
    }

    public int LogRetentionDays
    {
        get => _logRetentionDays;
        set => SetProperty(ref _logRetentionDays, Math.Clamp(value, 1, 3650));
    }

    public int TemporaryFileRetentionDays
    {
        get => _temporaryFileRetentionDays;
        set => SetProperty(ref _temporaryFileRetentionDays, Math.Clamp(value, 1, 3650));
    }

    public int CompletedWorkflowRetentionDays
    {
        get => _completedWorkflowRetentionDays;
        set => SetProperty(ref _completedWorkflowRetentionDays, Math.Clamp(value, 1, 3650));
    }

    public int BackupRetentionCount
    {
        get => _backupRetentionCount;
        set => SetProperty(ref _backupRetentionCount, Math.Clamp(value, 1, 100));
    }

    public bool ScheduledBackupsEnabled
    {
        get => _scheduledBackupsEnabled;
        set => SetProperty(ref _scheduledBackupsEnabled, value);
    }

    public string BackupScheduleDescription => _scheduledBackups.ScheduleDescription;
    public string BackupStatusSummary => _scheduledBackups.StatusSummary;

    public bool CreateAutomaticPreMigrationBackups
    {
        get => _createAutomaticPreMigrationBackups;
        set => SetProperty(ref _createAutomaticPreMigrationBackups, value);
    }


    public bool AllowTestDataReset
    {
        get => _allowTestDataReset;
        set
        {
            if (SetProperty(ref _allowTestDataReset, value))
            {
                ResetTestDataCommand.RaiseCanExecuteChanged();
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
                SaveCommand.RaiseCanExecuteChanged();
                InspectFieldsCommand.RaiseCanExecuteChanged();
                CreateBackupCommand.RaiseCanExecuteChanged();
                RestoreBackupCommand.RaiseCanExecuteChanged();
                CreateSupportPackageCommand.RaiseCanExecuteChanged();
                RunPreflightCommand.RaiseCanExecuteChanged();
                InstallUpdateCommand.RaiseCanExecuteChanged();
                ResetTestDataCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand BrowseTemplateCommand { get; }
    public RelayCommand BrowseCompletedFolderCommand { get; }
    public RelayCommand BrowseExcelCommand { get; }
    public RelayCommand BrowseAdobeCommand { get; }
    public RelayCommand BrowseScheduledBackupFolderCommand { get; }
    public AsyncRelayCommand InspectFieldsCommand { get; }
    public RelayCommand OpenDataFolderCommand { get; }
    public RelayCommand OpenBackupFolderCommand { get; }
    public RelayCommand OpenDiagnosticsFolderCommand { get; }
    public AsyncRelayCommand CreateBackupCommand { get; }
    public AsyncRelayCommand RestoreBackupCommand { get; }
    public AsyncRelayCommand CreateSupportPackageCommand { get; }
    public AsyncRelayCommand RunPreflightCommand { get; }
    public AsyncRelayCommand InstallUpdateCommand { get; }
    public AsyncRelayCommand ResetTestDataCommand { get; }

    public void Reload()
    {
        var settings = _settingsService.Current;
        TemplatePdfPath = settings.TemplatePdfPath;
        CompletedPdfFolder = settings.CompletedPdfFolder;
        ExcelExportPath = settings.ExcelExportPath;
        AdobeExecutablePath = settings.AdobeExecutablePath;
        MaximumDevicesPerForm = settings.MaximumDevicesPerForm;
        RequireSignatureForFinalization = settings.RequireSignatureForFinalization;
        LogRetentionDays = settings.LogRetentionDays;
        TemporaryFileRetentionDays = settings.TemporaryFileRetentionDays;
        CompletedWorkflowRetentionDays = settings.CompletedWorkflowRetentionDays;
        BackupRetentionCount = settings.BackupRetentionCount;
        ScheduledBackupsEnabled = settings.ScheduledBackupsEnabled;
        ScheduledBackupFolder = settings.ScheduledBackupFolder;
        CreateAutomaticPreMigrationBackups = settings.CreateAutomaticPreMigrationBackups;
        AllowTestDataReset = settings.AllowTestDataReset;
        OrganizationsText = string.Join(Environment.NewLine, settings.Organizations);

        FieldMappings.Clear();
        foreach (var pair in settings.PdfFieldMappings.OrderBy(pair => pair.Key))
        {
            FieldMappings.Add(new FieldMappingItem
            {
                LogicalName = pair.Key,
                PdfFieldName = pair.Value
            });
        }
        OnPropertyChanged(nameof(BackupStatusSummary));
    }

    private async Task SaveAsync()
    {
        try
        {
            IsBusy = true;
            var settings = BuildSettingsFromForm();
            await _settingsService.SaveAsync(settings);
            _logger.ConfigureRetention(settings.LogRetentionDays);
            _maintenance.Run(settings);
            _status.Message = "Settings saved.";
            OnPropertyChanged(nameof(BackupStatusSummary));

            MessageBox.Show(
                "Settings were saved. Organization changes are loaded when a new 1297 is created.",
                "Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError("Save settings", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private AppSettings BuildSettingsFromForm()
    {
        var organizations = OrganizationsText
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (organizations.Count == 0)
        {
            throw new InvalidOperationException(
                "Enter at least one organization. Put one organization on each line.");
        }

        var mappings = FieldMappings
            .Where(item => !string.IsNullOrWhiteSpace(item.LogicalName))
            .GroupBy(item => item.LogicalName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().PdfFieldName.Trim(),
                StringComparer.OrdinalIgnoreCase);
        mappings["PickupSignature"] = AppSettings.PickupSignatureFieldName;

        return new AppSettings
        {
            Theme = _settingsService.Current.Theme,
            TemplatePdfPath = TemplatePdfPath.Trim(),
            CompletedPdfFolder = CompletedPdfFolder.Trim(),
            ExcelExportPath = ExcelExportPath.Trim(),
            AdobeExecutablePath = AdobeExecutablePath.Trim(),
            MaximumDevicesPerForm = MaximumDevicesPerForm,
            RequireSignatureForFinalization = RequireSignatureForFinalization,
            LogRetentionDays = LogRetentionDays,
            TemporaryFileRetentionDays = TemporaryFileRetentionDays,
            CompletedWorkflowRetentionDays = CompletedWorkflowRetentionDays,
            BackupRetentionCount = BackupRetentionCount,
            ScheduledBackupsEnabled = ScheduledBackupsEnabled,
            ScheduledBackupFolder = ScheduledBackupFolder.Trim(),
            CreateAutomaticPreMigrationBackups = CreateAutomaticPreMigrationBackups,
            AllowTestDataReset = AllowTestDataReset,
            Organizations = organizations,
            PdfFieldMappings = mappings
        };
    }

    private async Task CreateBackupAsync()
    {
        try
        {
            IsBusy = true;
            var result = await _scheduledBackups.CreateFullBackupNowAsync();
            _status.Message = $"Full backup created: {result.BackupPath}";
            OnPropertyChanged(nameof(BackupStatusSummary));
            MessageBox.Show(
                $"A verified full backup was created with the database, settings, and " +
                $"{result.RecordFileCount} completed/archived 1297 PDF file(s).\n\n{result.BackupPath}",
                "Full backup complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError("Create backup", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StageRestoreAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select an Equipment Tracking backup",
            Filter = "Equipment Tracking backup (*.zip)|*.zip",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = Directory.Exists(_settingsService.ResolvePath(ScheduledBackupFolder))
                ? _settingsService.ResolvePath(ScheduledBackupFolder)
                : string.Empty
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var restoreSettings = MessageBox.Show(
            "Restore the settings stored in this backup too?\n\n" +
            "Choose No to restore only transaction data.",
            "Restore settings",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if (restoreSettings == MessageBoxResult.Cancel)
        {
            return;
        }

        var confirmed = MessageBox.Show(
            "The selected backup will be verified and staged. It will replace the current database the next time the application starts. " +
            "A safety copy of the current database will be created automatically. " +
            "If this is a differential backup, its Monday full backup must remain in the same folder. " +
            "PDF restoration is non-destructive: files not present in the backup are not deleted.\n\nContinue?",
            "Stage database restore",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await _backups.StageRestoreAsync(
                dialog.FileName,
                restoreSettings == MessageBoxResult.Yes,
                _settingsService.ResolvePath(CompletedPdfFolder));
            _status.Message = "Database restore staged. Restart the application to apply it.";
            MessageBox.Show(
                "The restore was verified and staged. Close and reopen Equipment Tracking Platform to apply it.",
                "Restore staged",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError("Stage database restore", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CreateSupportPackageAsync()
    {
        try
        {
            IsBusy = true;
            var path = await _supportPackages.CreateAsync();
            _status.Message = $"Support package created: {path}";
            MessageBox.Show(
                "A sanitized support package was created. It does not automatically include operational PDFs or the database.\n\n" + path,
                "Support package",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError("Create support package", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunPreflightAsync()
    {
        try
        {
            IsBusy = true;
            var report = await _preflight.RunAsync();
            var details = string.Join(
                Environment.NewLine + Environment.NewLine,
                report.Checks.Select(item => $"{item.Name}: {item.StatusText}\n{item.Message}"));
            DiagnosticDetailsDialog.Show(
                "Readiness check complete",
                $"{report.PassedCount} ready, {report.WarningCount} warning(s), {report.FailedCount} failed.",
                details);
            _status.Message = "Readiness check completed.";
        }
        catch (Exception ex)
        {
            ShowError("Run readiness check", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InstallUpdateAsync()
    {
        try
        {
            var pendingWorkflows = await _journals.GetPendingAsync();
            if (pendingWorkflows.Count > 0)
            {
                MessageBox.Show(
                    "Resolve or roll back every item on the Recovery page before installing an application update.",
                    "Update blocked by unfinished work",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }
        catch (Exception ex)
        {
            ShowError("Check update readiness", ex);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Select a newer Equipment Tracking Platform MSI",
            Filter = "Windows Installer package (*.msi)|*.msi",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var inspection = await _updates.InspectAsync(dialog.FileName);
            var verification = inspection.ManifestVerified
                ? "Companion manifest: SHA-256 and size verified"
                : "Companion manifest: not available";
            var warning = string.IsNullOrWhiteSpace(inspection.Warning)
                ? string.Empty
                : $"\n\nWarning: {inspection.Warning}";
            var confirmation = MessageBox.Show(
                $"Install Equipment Tracking Platform {inspection.ProductVersion}?\n\n" +
                $"File: {Path.GetFileName(inspection.MsiPath)}\n" +
                $"SHA-256: {inspection.Sha256}\n" +
                $"{verification}{warning}\n\n" +
                "A verified local database/settings backup will be created first. The application will then close and Windows Installer will request administrator approval.",
                "Install application update",
                MessageBoxButton.YesNo,
                string.IsNullOrWhiteSpace(inspection.Warning)
                    ? MessageBoxImage.Question
                    : MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            var schemaVersion = await _database.GetCurrentSchemaVersionAsync();
            var backupPath = await _backups.CreateBackupAsync(
                "before-update",
                schemaVersion,
                includeSettings: true);
            _logger.Information(
                $"Starting verified MSI update {inspection.ProductVersion}. Pre-update backup: {backupPath}");
            _updates.LaunchInstaller(inspection);
            _status.Message = "Windows Installer started. Closing the application for the update.";
            Application.Current.Shutdown(0);
        }
        catch (Exception ex)
        {
            ShowError("Install application update", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ResetTestDataAsync()
    {
        if (!AllowTestDataReset)
        {
            return;
        }

        var pending = await _journals.GetPendingAsync();
        if (pending.Count > 0)
        {
            MessageBox.Show(
                "Resolve or roll back all items on the Recovery page before resetting test data.",
                "Test-data reset blocked",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var dialog = new TextConfirmationDialog(
            "Reset all test database records",
            "This pilot/testing tool creates a verified backup, then removes all transactions, devices, audit rows, technician history, and file-artifact records from the local database. " +
            "It also removes the generated Excel workbook, temporary working files, and temporary two-copy print sheets. Completed and archived PDF files are deliberately NOT deleted because they may be official records and must be handled under the approved records policy. No Windows administrator permission is required.",
            "RESET TEST DATA")
        {
            Owner = Application.Current.MainWindow
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var schemaVersion = await _database.GetCurrentSchemaVersionAsync();
            var backupPath = await _backups.CreateBackupAsync(
                "before-test-reset",
                schemaVersion,
                includeSettings: true);

            await _database.ResetOperationalDataAsync();
            _journals.DeleteAll();
            DeleteDirectoryContents(_paths.WorkingDirectory);
            DeleteDirectoryContents(_paths.ReportsDirectory);
            DeleteDirectoryContents(_paths.PrintJobsDirectory);
            var excelPath = _settingsService.ResolvePath(_settingsService.Current.ExcelExportPath);
            if (File.Exists(excelPath))
            {
                File.Delete(excelPath);
            }

            _status.Message = "Test database records were reset after a verified backup.";
            MessageBox.Show(
                "The local database was reset. Completed and archived PDFs were not deleted.\n\nBackup:\n" + backupPath,
                "Test data reset complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError("Reset test data", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void DeleteDirectoryContents(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            File.Delete(file);
        }
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            Directory.Delete(child, recursive: true);
        }
    }

    private void BrowseTemplate()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select 1297 PDF template",
            Filter = "PDF files (*.pdf)|*.pdf",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() == true)
        {
            TemplatePdfPath = dialog.FileName;
        }
    }

    private void BrowseCompletedFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select completed PDF folder",
            Multiselect = false
        };
        if (dialog.ShowDialog() == true)
        {
            CompletedPdfFolder = dialog.FolderName;
        }
    }

    private void BrowseExcel()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Select Excel export workbook",
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            AddExtension = true,
            FileName = "EquipmentTracking.xlsx"
        };
        if (dialog.ShowDialog() == true)
        {
            ExcelExportPath = dialog.FileName;
        }
    }

    private void BrowseAdobe()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Adobe Acrobat executable",
            Filter = "Executable files (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() == true)
        {
            AdobeExecutablePath = dialog.FileName;
        }
    }

    private void BrowseScheduledBackupFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select scheduled backup folder",
            Multiselect = false
        };
        var current = _settingsService.ResolvePath(ScheduledBackupFolder);
        if (Directory.Exists(current))
        {
            dialog.InitialDirectory = current;
        }
        if (dialog.ShowDialog() == true)
        {
            ScheduledBackupFolder = dialog.FolderName;
        }
    }

    private void OpenBackupFolder()
    {
        var folder = _settingsService.ResolvePath(ScheduledBackupFolder);
        Directory.CreateDirectory(folder);
        _adobe.OpenFolder(folder);
    }

    private Task InspectFieldsAsync()
    {
        try
        {
            IsBusy = true;
            var resolvedTemplate = _settingsService.ResolvePath(TemplatePdfPath);
            var inspection = _pdfForms.InspectTemplate(
                resolvedTemplate,
                FieldMappings.Select(item => item.PdfFieldName).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray());

            var reportTimestamp = DateTime.Now.ToString(
                "yyyyMMdd-HHmmss",
                CultureInfo.InvariantCulture);
            var reportPath = Path.Combine(
                _paths.ReportsDirectory,
                $"PDF-Field-Names-{reportTimestamp}.txt");

            var builder = new StringBuilder()
                .AppendLine("PDF template inspection")
                .Append("Template: ").AppendLine(resolvedTemplate)
                .Append("Generated: ").AppendLine(DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture))
                .Append("NeedAppearances: ").AppendLine(inspection.NeedAppearances ? "true" : "false")
                .Append("JavaScript markers: ").AppendLine(inspection.ContainsJavaScriptMarkers ? "true" : "false")
                .Append("Signature fields: ").AppendLine(inspection.SignatureFieldCount.ToString(CultureInfo.InvariantCulture))
                .Append("Missing required fields: ").AppendLine(inspection.MissingRequiredFields.Count.ToString(CultureInfo.InvariantCulture))
                .AppendLine()
                .Append("Fields found: ").AppendLine(inspection.FieldNames.Count.ToString(CultureInfo.InvariantCulture))
                .AppendLine();

            foreach (var field in inspection.FieldNames)
            {
                builder.AppendLine(field);
            }

            if (inspection.MissingRequiredFields.Count > 0)
            {
                builder.AppendLine().AppendLine("Missing configured fields:");
                foreach (var field in inspection.MissingRequiredFields)
                {
                    builder.AppendLine(field);
                }
            }

            File.WriteAllText(reportPath, builder.ToString(), Encoding.UTF8);
            Process.Start(new ProcessStartInfo { FileName = reportPath, UseShellExecute = true });
            _status.Message = $"PDF field report created: {reportPath}";
        }
        catch (Exception ex)
        {
            ShowError("Inspect PDF fields", ex);
        }
        finally
        {
            IsBusy = false;
        }

        return Task.CompletedTask;
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

    private void ScheduledBackups_OnStatusChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            OnPropertyChanged(nameof(BackupStatusSummary));
            return;
        }

        dispatcher.BeginInvoke(new Action(() => OnPropertyChanged(nameof(BackupStatusSummary))));
    }
}
