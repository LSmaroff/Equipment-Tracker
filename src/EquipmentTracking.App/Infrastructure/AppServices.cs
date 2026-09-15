using EquipmentTracking.App.Models;
using EquipmentTracking.App.Services;
using EquipmentTracking.App.ViewModels;

namespace EquipmentTracking.App.Infrastructure;

public sealed class AppServices : IDisposable
{
    private bool _disposed;
    public AppServices()
    {
        SQLitePCL.Batteries_V2.Init();
        Paths = new AppPaths();
        Logger = new FileLogger(Paths);
        Status = new StatusService();
        Settings = new SettingsService(Paths, Logger);
        Theme = new ThemeService();
        NameParser = new CertificateNameParser();
        CacCertificates = new CacCertificateService(NameParser, Logger);
        BarcodeParser = new BarcodeParser();
        RecordCodes = new RecordCodeService();
        Updates = new UpdateService();
        FileNames = new FileNameService();
        PdfForms = new PdfFormService(Logger);
        SignatureExtraction = new SignatureExtractionService(NameParser, Logger);
        Backups = new BackupService(Paths, Logger);
        Database = new DatabaseService(Paths, Logger, Backups);
        KnownDevices = new KnownDeviceCatalog();
        DeviceRecognition = new DeviceRecognitionService(
            Database,
            BarcodeParser,
            KnownDevices);
        ScheduledBackups = new ScheduledBackupService(
            Paths,
            Settings,
            Database,
            Backups,
            Status,
            Logger);
        Journals = new WorkflowJournalService(Paths, Logger);
        Excel = new ExcelExportService(Database, Settings, Logger);
        Adobe = new AdobeService(Settings, Logger);
        PrintJobs = new PrintJobService(Paths, Logger);
        Workflow = new TransactionWorkflowService(
            Paths,
            Settings,
            PdfForms,
            SignatureExtraction,
            Database,
            Excel,
            FileNames,
            Journals,
            Logger);
        Preflight = new PreflightService(
            Paths,
            Settings,
            Database,
            PdfForms,
            CacCertificates,
            Logger);
        SupportPackages = new SupportPackageService(
            Paths,
            Settings,
            Database,
            PdfForms,
            CacCertificates,
            Journals,
            Preflight,
            Logger);
        Maintenance = new MaintenanceService(Paths, Backups, Journals, Logger);
    }

    public AppPaths Paths { get; }
    public FileLogger Logger { get; }
    public StatusService Status { get; }
    public SettingsService Settings { get; }
    public ThemeService Theme { get; }
    public CertificateNameParser NameParser { get; }
    public CacCertificateService CacCertificates { get; }
    public BarcodeParser BarcodeParser { get; }
    public RecordCodeService RecordCodes { get; }
    public UpdateService Updates { get; }
    public FileNameService FileNames { get; }
    public PdfFormService PdfForms { get; }
    public SignatureExtractionService SignatureExtraction { get; }
    public BackupService Backups { get; }
    public DatabaseService Database { get; }
    public KnownDeviceCatalog KnownDevices { get; }
    public DeviceRecognitionService DeviceRecognition { get; }
    public ScheduledBackupService ScheduledBackups { get; }
    public WorkflowJournalService Journals { get; }
    public ExcelExportService Excel { get; }
    public AdobeService Adobe { get; }
    public PrintJobService PrintJobs { get; }
    public TransactionWorkflowService Workflow { get; }
    public PreflightService Preflight { get; }
    public SupportPackageService SupportPackages { get; }
    public MaintenanceService Maintenance { get; }
    public PreflightReport? StartupPreflightReport { get; private set; }
    public int PendingRecoveryCount { get; private set; }

    public async Task InitializeAsync()
    {
        Paths.EnsureDirectories();
        await Backups.ApplyPendingRestoreIfAnyAsync();
        await Settings.LoadAsync();
        Logger.ConfigureRetention(Settings.Current.LogRetentionDays);
        Theme.Apply(Settings.Current.Theme);
        await Database.InitializeAsync(Settings.Current.CreateAutomaticPreMigrationBackups);
        Maintenance.Run(Settings.Current);
        StartupPreflightReport = await Preflight.RunAsync();
        ScheduledBackups.Start();
        PendingRecoveryCount = (await Journals.GetPendingAsync()).Count;
        if (PendingRecoveryCount > 0)
        {
            Status.Message = $"{PendingRecoveryCount} interrupted workflow(s) require attention in Recovery.";
        }
    }

    public MainWindowViewModel CreateMainWindowViewModel()
    {
        var dashboard = new DashboardViewModel(
            Database,
            Workflow,
            Adobe,
            PrintJobs,
            RecordCodes,
            Status,
            Logger);
        var intake = new IntakeViewModel(
            Workflow,
            CacCertificates,
            BarcodeParser,
            DeviceRecognition,
            Adobe,
            Settings,
            Database,
            Excel,
            Status,
            Logger);
        var returns = new ReturnsViewModel(
            Database,
            Excel,
            Adobe,
            Workflow,
            RecordCodes,
            Status,
            Logger);
        var settings = new SettingsViewModel(
            Settings,
            PdfForms,
            Adobe,
            Paths,
            Backups,
            ScheduledBackups,
            Database,
            SupportPackages,
            Preflight,
            Maintenance,
            Journals,
            Updates,
            Status,
            Logger);
        var recovery = new RecoveryViewModel(
            Journals,
            Workflow,
            Adobe,
            Status,
            Logger);
        var howToUse = new HowToUseViewModel();

        return new MainWindowViewModel(
            dashboard,
            intake,
            returns,
            recovery,
            settings,
            howToUse,
            Excel,
            Settings,
            Theme,
            Status,
            Logger,
            PendingRecoveryCount);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ScheduledBackups.Dispose();
        Journals.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

}
