namespace EquipmentTracking.App.Models;

public sealed class UpdatePackageInspection
{
    public string MsiPath { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public Version ProductVersion { get; init; } = new();
    public string UpgradeCode { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public bool ManifestFound { get; init; }
    public bool ManifestVerified { get; init; }
    public bool AuthenticodeSigned { get; init; }
    public string ReleaseUse { get; init; } = string.Empty;
    public string Warning { get; init; } = string.Empty;
}
