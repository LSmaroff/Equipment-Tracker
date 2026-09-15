namespace EquipmentTracking.App.Services;

public static class RankCatalog
{
    public static IReadOnlyList<string> Values { get; } =
    [
        "AB", "Amn", "A1C", "SrA", "SSgt", "TSgt", "MSgt", "SMSgt", "CMSgt",
        "2d Lt", "1st Lt", "Capt", "Maj", "Lt Col", "Col", "Brig Gen", "Maj Gen",
        "Lt Gen", "Gen", "Civilian", "Contractor"
    ];

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return Values.FirstOrDefault(rank =>
                   string.Equals(rank, trimmed, StringComparison.OrdinalIgnoreCase))
               ?? trimmed;
    }
}
