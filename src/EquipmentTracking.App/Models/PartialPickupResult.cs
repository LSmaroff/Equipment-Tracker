namespace EquipmentTracking.App.Models;

public sealed class PartialPickupResult
{
    public required PickupReceipt PickupDocument { get; init; }
    public required EquipmentTransaction Transaction { get; init; }
    public required SignatureInfo PickupSignature { get; init; }
    public string SignedReceiptPath => PickupDocument.PdfPath;
    public string ExcelExportPath { get; init; } = string.Empty;
    public int UpdatedCount { get; init; }
    public int RemainingCount { get; init; }
    public bool Archived { get; init; }
    public bool WasAlreadyCommitted { get; init; }
}
