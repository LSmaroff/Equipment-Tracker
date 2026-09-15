using EquipmentTracking.App.Infrastructure;

namespace EquipmentTracking.App.Models;

public sealed class FieldMappingItem : ObservableObject
{
    private string _logicalName = string.Empty;
    private string _pdfFieldName = string.Empty;

    public string LogicalName
    {
        get => _logicalName;
        set => SetProperty(ref _logicalName, value ?? string.Empty);
    }

    public string PdfFieldName
    {
        get => _pdfFieldName;
        set => SetProperty(ref _pdfFieldName, value ?? string.Empty);
    }
}
