namespace EquipmentTracking.App.Models;

public sealed class SignatureInfo
{
    public bool SignatureFound { get; init; }
    public string SignerName { get; init; } = string.Empty;
    public string CertificateSubject { get; init; } = string.Empty;
    public string CertificateThumbprint { get; init; } = string.Empty;
    public DateTimeOffset? SigningTime { get; init; }
    public CustomerIdentity Identity { get; init; } = new();
    public string DiagnosticMessage { get; init; } = string.Empty;
}
