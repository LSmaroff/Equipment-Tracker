namespace EquipmentTracking.App.Services;

public static class DeviceStatusCatalog
{
    public const string InShop = "In shop";
    public const string ReImaging = "Re-imaging";
    public const string ReadyForPickup = "Ready for pickup";
    public const string Troubleshooting = "Troubleshooting";
    public const string Returned = "Returned";

    public static IReadOnlyList<string> ActiveStatuses { get; } =
        [InShop, ReImaging, ReadyForPickup, Troubleshooting];

    public static IReadOnlyList<string> Values { get; } =
        [InShop, ReImaging, ReadyForPickup, Troubleshooting, Returned];

    public static bool IsValidActiveStatus(string? status)
    {
        return ActiveStatuses.Contains(status ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsValidStatus(string? status)
    {
        return Values.Contains(status ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }
}
