namespace EquipmentTracking.App.Models;

public sealed class AppSettings
{
    public const string PickupSignatureFieldName = "Pickup Signature";

    public string Theme { get; set; } = "Dark";
    public string TemplatePdfPath { get; set; } = @"Templates\1297-58SOW-SC-TEMPLATE.pdf";
    public string CompletedPdfFolder { get; set; } =
        @"%USERPROFILE%\Documents\Equipment Tracking\Completed 1297s";
    public string ExcelExportPath { get; set; } =
        @"%USERPROFILE%\Documents\Equipment Tracking\EquipmentTracking.xlsx";
    public string AdobeExecutablePath { get; set; } = string.Empty;
    public int MaximumDevicesPerForm { get; set; } = 10;
    public bool RequireSignatureForFinalization { get; set; } = true;
    public int LogRetentionDays { get; set; } = 30;
    public int TemporaryFileRetentionDays { get; set; } = 7;
    public int CompletedWorkflowRetentionDays { get; set; } = 30;
    public int BackupRetentionCount { get; set; } = 5;
    public bool ScheduledBackupsEnabled { get; set; } = true;
    public string ScheduledBackupFolder { get; set; } =
        @"%USERPROFILE%\Documents\Equipment Tracking\Backups";
    public bool CreateAutomaticPreMigrationBackups { get; set; } = true;
    public bool AllowTestDataReset { get; set; }
    public List<string> Organizations { get; set; } = ["58 SOW"];
    public Dictionary<string, string> PdfFieldMappings { get; set; } =
        CreateDefaultPdfFieldMappings();

    public static Dictionary<string, string> CreateDefaultPdfFieldMappings()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TechnicianSignature"] = "ISSUED BY SIGNATURE",
            ["PhoneNumber"] = "DUTY PHONE",
            ["CustomerSignature"] = "ISSUED TO SIGNATURE",
            // The approved template's AcroForm name is misleading: this widget is
            // physically located in the ISSUED TO: NAME, GRADE, ORGN row. Retain
            // the legacy logical key so existing settings remain compatible.
            ["TechnicianNameGrade"] = "TechnicianName",
            ["Organization"] = "ORGN",
            ["IssueDate"] = "DATE OF ISSUE",
            ["ReturnDate"] = "RETURN DATE",
            ["Device1"] = "Device1",
            ["Device2"] = "Device2",
            ["Device3"] = "Device3",
            ["Device4"] = "Device4",
            ["Device5"] = "Device5",
            ["Device6"] = "Device6",
            ["Device7"] = "Device7",
            ["Device8"] = "Device8",
            ["Device9"] = "Device9",
            ["Device10"] = "Device10",
            ["TicketNumber"] = "TicketNumber",
            ["PickupSignature"] = PickupSignatureFieldName,
            ["Quantity"] = "QNTY"
        };
    }

    public AppSettings Clone()
    {
        return new AppSettings
        {
            Theme = Theme,
            TemplatePdfPath = TemplatePdfPath,
            CompletedPdfFolder = CompletedPdfFolder,
            ExcelExportPath = ExcelExportPath,
            AdobeExecutablePath = AdobeExecutablePath,
            MaximumDevicesPerForm = MaximumDevicesPerForm,
            RequireSignatureForFinalization = RequireSignatureForFinalization,
            LogRetentionDays = LogRetentionDays,
            TemporaryFileRetentionDays = TemporaryFileRetentionDays,
            CompletedWorkflowRetentionDays = CompletedWorkflowRetentionDays,
            BackupRetentionCount = BackupRetentionCount,
            ScheduledBackupsEnabled = ScheduledBackupsEnabled,
            ScheduledBackupFolder = ScheduledBackupFolder,
            CreateAutomaticPreMigrationBackups = CreateAutomaticPreMigrationBackups,
            AllowTestDataReset = AllowTestDataReset,
            Organizations = Organizations.ToList(),
            PdfFieldMappings = new Dictionary<string, string>(
                PdfFieldMappings,
                StringComparer.OrdinalIgnoreCase)
        };
    }
}
