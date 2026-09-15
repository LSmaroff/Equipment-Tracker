namespace EquipmentTracking.App.Services;

public static class DashboardSearchInputRouter
{
    public const int MaximumQueryLength = 256;

    public static bool TryCreateInitialQuery(
        string? input,
        bool commandModifierPressed,
        bool focusedControlOwnsTextInput,
        out string query)
    {
        query = string.Empty;
        if (commandModifierPressed ||
            focusedControlOwnsTextInput ||
            string.IsNullOrWhiteSpace(input) ||
            input.Any(char.IsControl))
        {
            return false;
        }

        query = input.Length <= MaximumQueryLength
            ? input
            : input[..MaximumQueryLength];
        return true;
    }
}
