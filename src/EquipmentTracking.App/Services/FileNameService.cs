using System.IO;
using System.Text;
using EquipmentTracking.App.Models;

namespace EquipmentTracking.App.Services;

public sealed class FileNameService
{
    private static readonly HashSet<string> ReservedWindowsNames = new(
        [
            "CON", "PRN", "AUX", "NUL", "CLOCK$",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        ],
        StringComparer.OrdinalIgnoreCase);
    public string BuildFinalPdfName(
        CustomerIdentity identity,
        DateTimeOffset timestamp,
        string transactionId)
    {
        var first = NormalizeComponent(identity.FirstName, "unknown");
        var middle = NormalizeComponent(identity.MiddleInitial, string.Empty);
        var last = NormalizeComponent(identity.LastName, "signer");
        var shortId = transactionId
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? transactionId;

        var namePart = string.IsNullOrWhiteSpace(middle)
            ? $"{first}.{last}"
            : $"{first}.{middle}.{last}";

        // Person-name components remain lowercase, while the transaction suffix is
        // normalized to uppercase so generated filenames match the documented format
        // and the FileNameService unit-test contract.
        var normalizedShortId = NormalizeComponent(shortId, "tx").ToUpperInvariant();

        return $"{namePart}-{timestamp:MM.dd.yy-HHmmss}-{normalizedShortId}-1297.pdf";
    }

    public string EnsureUniquePath(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var index = 2; index < 10_000; index++)
        {
            candidate = Path.Combine(directory, $"{baseName}-{index}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("A unique output filename could not be generated.");
    }

    public string BuildClosedPdfName(string existingPath, DateTimeOffset closedAt)
    {
        var baseName = NormalizeComponent(
            Path.GetFileNameWithoutExtension(existingPath),
            "closed-1297");
        return $"{baseName}-CLOSED-{closedAt:MM.dd.yy-HHmmss}.pdf";
    }

    public string NormalizeDirectoryName(string? value, string fallback = "Unspecified Organization")
    {
        var normalized = NormalizeComponent(value, NormalizeComponent(fallback, "unspecified"));
        if (normalized is "." or "..")
        {
            return "unspecified";
        }

        return normalized;
    }

    private static string NormalizeComponent(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder();

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (invalid.Contains(character))
            {
                continue;
            }

            builder.Append(char.IsWhiteSpace(character) ? '-' : character);
        }

        var result = builder.ToString().Trim('.', '-', ' ');
        if (string.IsNullOrWhiteSpace(result))
        {
            result = fallback;
        }

        if (ReservedWindowsNames.Contains(result))
        {
            result = $"_{result}";
        }

        const int maximumComponentLength = 100;
        return result.Length <= maximumComponentLength
            ? result
            : result[..maximumComponentLength].TrimEnd('.', '-', ' ');
    }
}
