using EquipmentTracking.App.Infrastructure;

namespace EquipmentTracking.App.Models;

public sealed class DeviceRecord : ObservableObject
{
    private long _id;
    private string _partNumber = string.Empty;
    private string _model = string.Empty;
    private string _serialNumber = string.Empty;
    private string _assetTag = string.Empty;
    private string _rawScanValue = string.Empty;
    private string _status = "In shop";
    private DateTimeOffset? _returnedAt;
    private string _returnCondition = string.Empty;
    private string _returnNotes = string.Empty;

    public long Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string PartNumber
    {
        get => _partNumber;
        set => SetProperty(ref _partNumber, value?.Trim() ?? string.Empty);
    }

    public string Model
    {
        get => _model;
        set => SetProperty(ref _model, value?.Trim() ?? string.Empty);
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

    public string RawScanValue
    {
        get => _rawScanValue;
        set => SetProperty(ref _rawScanValue, value ?? string.Empty);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value ?? string.Empty);
    }

    public DateTimeOffset? ReturnedAt
    {
        get => _returnedAt;
        set => SetProperty(ref _returnedAt, value);
    }

    public string ReturnCondition
    {
        get => _returnCondition;
        set => SetProperty(ref _returnCondition, value ?? string.Empty);
    }

    public string ReturnNotes
    {
        get => _returnNotes;
        set => SetProperty(ref _returnNotes, value ?? string.Empty);
    }
}
