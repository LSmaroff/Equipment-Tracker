using System.Text.RegularExpressions;
using QRCoder;

namespace EquipmentTracking.App.Services;

public sealed partial class RecordCodeService
{
    public const string PayloadPrefix = "ETP1297:";

    public string BuildPayload(string transactionId)
    {
        if (!TryNormalizeTransactionId(transactionId, out var normalized))
        {
            throw new ArgumentException("The 1297 ID is invalid.", nameof(transactionId));
        }

        return PayloadPrefix + normalized;
    }

    public bool TryParse(string? value, out string transactionId)
    {
        transactionId = string.Empty;
        var candidate = value?.Trim() ?? string.Empty;
        if (candidate.StartsWith(PayloadPrefix, StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[PayloadPrefix.Length..].Trim();
        }

        return TryNormalizeTransactionId(candidate, out transactionId);
    }

    public bool LooksLikeRecordCode(string? value) => TryParse(value, out _);

    public byte[] GeneratePng(string transactionId, int pixelsPerModule = 8)
    {
        var payload = BuildPayload(transactionId);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        using var code = new PngByteQRCode(data);
        return code.GetGraphic(Math.Clamp(pixelsPerModule, 2, 32), drawQuietZones: true);
    }

    private static bool TryNormalizeTransactionId(string? value, out string normalized)
    {
        normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!TransactionIdRegex().IsMatch(normalized))
        {
            normalized = string.Empty;
            return false;
        }

        return true;
    }

    [GeneratedRegex(@"^TX-\d{8}-\d{6}-[A-F0-9]{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex TransactionIdRegex();
}
