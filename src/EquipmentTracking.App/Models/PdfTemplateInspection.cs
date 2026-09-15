namespace EquipmentTracking.App.Models;

public sealed class PdfTemplateInspection
{
    public string PdfPath { get; init; } = string.Empty;
    public IReadOnlyList<string> FieldNames { get; init; } = [];
    public int SignatureFieldCount { get; init; }
    public bool NeedAppearances { get; init; }
    public bool ContainsJavaScriptMarkers { get; init; }
    public IReadOnlyList<string> MissingRequiredFields { get; init; } = [];
    public bool IsReady => MissingRequiredFields.Count == 0 && !NeedAppearances && !ContainsJavaScriptMarkers;
}
