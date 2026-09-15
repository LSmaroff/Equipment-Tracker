using EquipmentTracking.App.Infrastructure;

namespace EquipmentTracking.App.Models;

public sealed class DeviceEntryRow : ObservableObject
{
    private int _rowNumber;
    private string _scanInput = string.Empty;
    private string _partNumber = string.Empty;
    private string _modelName = string.Empty;
    private string _serialNumber = string.Empty;
    private string _assetTag = string.Empty;
    private string _feedback = "Ready to scan.";
    private bool _isCommitted;

    public Guid EntryId { get; } = Guid.NewGuid();
    public DeviceRecord? CommittedDevice { get; set; }

    public int RowNumber
    {
        get => _rowNumber;
        set => SetProperty(ref _rowNumber, value);
    }

    public string ScanInput
    {
        get => _scanInput;
        set => SetProperty(ref _scanInput, value ?? string.Empty);
    }

    public string PartNumber
    {
        get => _partNumber;
        set => SetProperty(ref _partNumber, value?.Trim() ?? string.Empty);
    }

    public string ModelName
    {
        get => _modelName;
        // Preserve the live editing value so a trailing space can remain long
        // enough for the operator to type the next word. Commit/save boundaries
        // still trim surrounding whitespace before persistence.
        set => SetProperty(ref _modelName, value ?? string.Empty);
    }

    public string SerialNumber
    {
        get => _serialNumber;
        set => SetProperty(ref _serialNumber, value?.Trim() ?? string.Empty);
    }

    public string AssetTag
    {
        get => _assetTag;
        set => SetProperty(ref _assetTag, value?.Trim() ?? string.Empty);
    }

    public string Feedback
    {
        get => _feedback;
        set => SetProperty(ref _feedback, value ?? string.Empty);
    }

    public bool IsCommitted
    {
        get => _isCommitted;
        set
        {
            if (SetProperty(ref _isCommitted, value))
            {
                OnPropertyChanged(nameof(ActionLabel));
            }
        }
    }

    public string ActionLabel => IsCommitted ? "Update" : "Add";
}
