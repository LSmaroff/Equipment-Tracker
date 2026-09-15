namespace EquipmentTracking.App.Models;

public sealed class PdfFillResult
{
    public int ConfiguredFieldCount { get; init; }
    public int FieldsFilled { get; init; }
    public List<string> MissingPdfFields { get; init; } = [];
}
