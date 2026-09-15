namespace EquipmentTracking.App.Models;

public sealed class ModelCatalogEntry
{
    public string PartNumber { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset LastUsedAt { get; init; }
    public int UseCount { get; init; }
}
