namespace EquipmentTracking.App.Models;

public sealed class FinalizationResult
{
    public EquipmentTransaction Transaction { get; init; } = new();
    public SignatureInfo Signature { get; init; } = new();
    public string FinalPdfPath { get; init; } = string.Empty;
    public string ExcelExportPath { get; init; } = string.Empty;
}
