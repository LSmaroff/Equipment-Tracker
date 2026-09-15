namespace EquipmentTracking.App.Models;

public sealed class CacCertificateCandidate
{
    public string ReaderName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Issuer { get; init; } = string.Empty;
    public string Thumbprint { get; init; } = string.Empty;
    public DateTime NotAfter { get; init; }
    public int Score { get; init; }
    public CustomerIdentity Identity { get; init; } = new();
}
