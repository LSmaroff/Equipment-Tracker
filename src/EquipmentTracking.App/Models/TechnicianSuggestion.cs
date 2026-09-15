namespace EquipmentTracking.App.Models;

public sealed class TechnicianSuggestion
{
    public string NameGrade { get; init; } = string.Empty;
    public DateTimeOffset LastUsedAt { get; init; }
    public int UseCount { get; init; }
}
