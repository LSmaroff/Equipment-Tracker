namespace EquipmentTracking.App.Models;

public sealed class CustomerIdentity
{
    public string Rank { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string MiddleInitial { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;

    public string FullName
    {
        get
        {
            var parts = new[] { FirstName, MiddleInitial, LastName }
                .Where(value => !string.IsNullOrWhiteSpace(value));
            return string.Join(' ', parts);
        }
    }

    public string DisplayName
    {
        get
        {
            var lastFirst = string.IsNullOrWhiteSpace(LastName)
                ? FullName
                : $"{LastName}, {FirstName}".TrimEnd(' ', ',');
            if (!string.IsNullOrWhiteSpace(MiddleInitial))
            {
                lastFirst = $"{lastFirst} {MiddleInitial}";
            }

            return string.IsNullOrWhiteSpace(Rank)
                ? lastFirst.Trim()
                : $"{Rank.Trim()} {lastFirst.Trim()}";
        }
    }

    public bool HasUsableName =>
        !string.IsNullOrWhiteSpace(FirstName) &&
        !string.IsNullOrWhiteSpace(LastName);
}
