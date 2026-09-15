namespace EquipmentTracking.App.Models;

public sealed class CloseoutResult
{
    public required EquipmentTransaction Transaction { get; init; }
    public required string ArchivedPdfPath { get; init; }
    public string ExcelExportPath { get; init; } = string.Empty;
    public required SignatureInfo PickupSignature { get; init; }
}
