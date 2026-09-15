using System.Text.RegularExpressions;
using EquipmentTracking.App.Models;

namespace EquipmentTracking.App.Services;

public sealed partial class BarcodeParser
{
    private static readonly string[] PartNumberKeys =
        ["MODEL", "MDL", "PRODUCT", "PART", "PARTNUMBER", "PN", "P/N", "240"];
    private static readonly string[] SerialKeys =
        ["SERIAL", "SER", "SN", "S/N", "21"];
    private static readonly string[] AssetKeys =
        ["ASSET", "ASSETTAG", "TAG", "PROPERTY", "EQUIPMENT"];

    public BarcodeParseResult Parse(string? input)
    {
        var raw = input ?? string.Empty;
        var normalizedInput = raw.Trim();

        if (string.IsNullOrWhiteSpace(normalizedInput))
        {
            return new BarcodeParseResult
            {
                RawValue = raw,
                Message = "The scan was empty."
            };
        }

        var iuid = ParseIsoIec15434(normalizedInput, raw);
        if (iuid.Parsed)
        {
            return iuid;
        }

        var dictionary = ParseKeyValuePairs(normalizedInput);
        var partNumber = FindValue(dictionary, PartNumberKeys);
        var serial = FindValue(dictionary, SerialKeys);
        var asset = FindValue(dictionary, AssetKeys);

        if (string.IsNullOrWhiteSpace(serial))
        {
            var gs1 = ParseGs1(normalizedInput, raw);
            partNumber = FirstNonEmpty(partNumber, gs1.PartNumber);
            serial = FirstNonEmpty(serial, gs1.SerialNumber);
            asset = FirstNonEmpty(asset, gs1.AssetTag);
        }

        if (string.IsNullOrWhiteSpace(partNumber) && string.IsNullOrWhiteSpace(serial))
        {
            var parts = DelimitedPartsRegex().Split(normalizedInput)
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            if (parts.Length is 2 or 3)
            {
                partNumber = parts[0];
                serial = parts[1];
                asset = parts.Length == 3 ? parts[2] : string.Empty;
            }
        }

        var parsed = !string.IsNullOrWhiteSpace(serial) || !string.IsNullOrWhiteSpace(partNumber);

        return new BarcodeParseResult
        {
            Parsed = parsed,
            PartNumber = partNumber,
            SerialNumber = serial,
            AssetTag = asset,
            RawValue = raw,
            Message = parsed
                ? "Scan parsed. Review the part number and serial number."
                : "The scan was captured, but its format is unknown. Enter the part number and serial number manually."
        };
    }

    private static BarcodeParseResult ParseIsoIec15434(string input, string raw)
    {
        var normalized = NormalizeScannerControlTokens(input);
        var headerIndex = normalized.IndexOf("[)>", StringComparison.Ordinal);
        if (headerIndex >= 0)
        {
            normalized = normalized[(headerIndex + 3)..];
        }
        else if (normalized.StartsWith("[)", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        var identifierIndex = FindFirstDataIdentifier(normalized);
        if (identifierIndex < 0)
        {
            return new BarcodeParseResult { RawValue = raw };
        }

        var payload = normalized[identifierIndex..].Trim();
        payload = payload.TrimStart('\u001e');

        if (payload.StartsWith("06", StringComparison.Ordinal))
        {
            payload = payload[2..].TrimStart('\u001d');
        }

        var segments = payload
            .Split(['\u001d', '\u001e', '\u0004'], StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Trim())
            .Where(segment => segment.Length > 0)
            .ToArray();

        if (segments.Length > 1)
        {
            var segmentedResult = ParseIso15434Segments(segments, raw);
            if (segmentedResult.Parsed)
            {
                return segmentedResult;
            }
        }

        var compact = ControlCharacterRegex().Replace(payload, string.Empty).Trim();

        var eighteenSIndex = compact.IndexOf("18S", StringComparison.OrdinalIgnoreCase);
        if (eighteenSIndex >= 0 &&
            TryParse18S(
                compact[eighteenSIndex..],
                out var cageCode,
                out var cageSerial))
        {
            return BuildIuidResult(
                partNumber: string.Empty,
                serialNumber: cageSerial,
                cageCode: cageCode,
                raw: raw,
                message: "DoD IUID 18S scan parsed. The CAGE code was ignored. This barcode does not contain a part number, so enter the part number manually.");
        }

        var seventeenVIndex = compact.IndexOf("17V", StringComparison.OrdinalIgnoreCase);
        if (seventeenVIndex >= 0 &&
            TryParseEnterprisePartSerial(
                compact[seventeenVIndex..],
                out var partNumber,
                out var serialNumber,
                out var enterpriseCode))
        {
            return BuildIuidResult(partNumber, serialNumber, enterpriseCode, raw);
        }

        return new BarcodeParseResult { RawValue = raw };
    }

    private static BarcodeParseResult ParseIso15434Segments(
        IEnumerable<string> segments,
        string raw)
    {
        var partNumber = string.Empty;
        var serialNumber = string.Empty;
        var cageCode = string.Empty;

        foreach (var segment in segments)
        {
            if (segment.StartsWith("17V", StringComparison.OrdinalIgnoreCase) &&
                segment.Length >= 8)
            {
                cageCode = segment.Substring(3, 5).Trim();
            }
            else if (segment.StartsWith("30P", StringComparison.OrdinalIgnoreCase))
            {
                partNumber = segment[3..].Trim();
            }
            else if (segment.StartsWith("1P", StringComparison.OrdinalIgnoreCase))
            {
                partNumber = segment[2..].Trim();
            }
            else if (segment.StartsWith("18S", StringComparison.OrdinalIgnoreCase) &&
                      TryParse18S(segment, out var segmentCage, out var cageSerial))
            {
                cageCode = segmentCage;
                serialNumber = cageSerial;
            }
            else if (segment.StartsWith("S", StringComparison.OrdinalIgnoreCase) &&
                     segment.Length > 1)
            {
                serialNumber = segment[1..].Trim();
            }
        }

        if (partNumber.Length == 0 && serialNumber.Length == 0)
        {
            return new BarcodeParseResult { RawValue = raw };
        }

        var message = partNumber.Length == 0 && serialNumber.Length > 0
            ? "DoD IUID scan parsed. The serial number was captured, but this barcode does not contain a part number. Enter the part number manually."
            : "DoD IUID scan parsed. The CAGE code was ignored; the part number and serial number were captured.";

        return BuildIuidResult(partNumber, serialNumber, cageCode, raw, message);
    }

    private static bool TryParse18S(
        string compact,
        out string cageCode,
        out string serialNumber)
    {
        cageCode = string.Empty;
        serialNumber = string.Empty;
        if (!compact.StartsWith("18S", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = compact[3..].Trim();
        if (value.Length <= 5)
        {
            return false;
        }

        // DI 18S is CAGE (five characters) followed by the serial number.
        cageCode = value[..5].Trim();
        serialNumber = value[5..].Trim();
        return cageCode.Length == 5 && serialNumber.Length > 0;
    }

    private static bool TryParseEnterprisePartSerial(
        string compact,
        out string partNumber,
        out string serialNumber,
        out string cageCode)
    {
        partNumber = string.Empty;
        serialNumber = string.Empty;
        cageCode = string.Empty;

        var value = compact;
        if (value.StartsWith("17V", StringComparison.OrdinalIgnoreCase))
        {
            // DI 17V is followed by a five-character CAGE code.
            if (value.Length <= 8)
            {
                return false;
            }

            cageCode = value.Substring(3, 5).Trim();
            value = value[8..];
        }

        // Some manufacturers place the serial DI before the part DI.
        // Example: 17V{CAGE}S{serial}1P{part}.
        if (value.StartsWith("S", StringComparison.OrdinalIgnoreCase))
        {
            var partMarker = value.IndexOf("1P", 1, StringComparison.OrdinalIgnoreCase);
            if (partMarker > 1 && partMarker + 2 < value.Length)
            {
                serialNumber = value[1..partMarker].Trim();
                partNumber = value[(partMarker + 2)..].Trim();
                return PartNumberValueRegex().IsMatch(partNumber) &&
                       SerialValueRegex().IsMatch(serialNumber);
            }
        }

        if (value.StartsWith("30P", StringComparison.OrdinalIgnoreCase))
        {
            value = value[3..];
        }
        else if (value.StartsWith("1P", StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
        }
        else
        {
            return false;
        }

        return TrySplitPartAndSerial(value, out partNumber, out serialNumber);
    }

    private static bool TrySplitPartAndSerial(
        string combinedValue,
        out string partNumber,
        out string serialNumber)
    {
        partNumber = string.Empty;
        serialNumber = string.Empty;

        var candidates = new List<(string Part, string Serial, int Score)>();
        for (var index = 1; index < combinedValue.Length - 1; index++)
        {
            if (combinedValue[index] != 'S' && combinedValue[index] != 's')
            {
                continue;
            }

            var part = combinedValue[..index].Trim();
            var serial = combinedValue[(index + 1)..].Trim();

            if (part.Length is < 1 or > 32 || serial.Length is < 2 or > 30)
            {
                continue;
            }

            if (!PartNumberValueRegex().IsMatch(part) || !SerialValueRegex().IsMatch(serial))
            {
                continue;
            }

            var score = 0;
            score += Math.Min(part.Length, 20);
            score += Math.Min(serial.Length, 20);

            if (serial.Length >= 6)
            {
                score += 40;
            }

            if (serial.All(char.IsDigit))
            {
                score += 100;
            }

            if (char.IsDigit(serial[0]))
            {
                score += 25;
            }

            if (part.Contains('-'))
            {
                score += 20;
            }

            if (part.Contains('/'))
            {
                score += 10;
            }

            if (serial.Contains('-') ||
                serial.Contains('/'))
            {
                score -= 10;
            }

            candidates.Add((part, serial, score));
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        var best = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Serial.Length)
            .ThenByDescending(candidate => candidate.Part.Length)
            .First();

        partNumber = best.Part;
        serialNumber = best.Serial;
        return true;
    }

    private static BarcodeParseResult BuildIuidResult(
        string partNumber,
        string serialNumber,
        string cageCode,
        string raw,
        string? message = null)
    {
        var part = partNumber.Trim();
        var serial = serialNumber.Trim();
        var parsed = part.Length > 0 || serial.Length > 0;

        return new BarcodeParseResult
        {
            Parsed = parsed,
            PartNumber = part,
            SerialNumber = serial,
            CageCode = cageCode.Trim(),
            RawValue = raw,
            Message = message ?? (part.Length > 0 && serial.Length > 0
                ? "DoD IUID scan parsed. The CAGE code was ignored; the part number and serial number were captured."
                : "The IUID scan was captured, but it did not contain both a part number and a serial number.")
        };
    }

    private static string NormalizeScannerControlTokens(string input)
    {
        return input
            .Replace("\\0000", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("½RSW", "\u001e", StringComparison.OrdinalIgnoreCase)
            .Replace("½GSW", "\u001d", StringComparison.OrdinalIgnoreCase)
            .Replace("«RS»", "\u001e", StringComparison.OrdinalIgnoreCase)
            .Replace("«GS»", "\u001d", StringComparison.OrdinalIgnoreCase)
            .Replace("<RS>", "\u001e", StringComparison.OrdinalIgnoreCase)
            .Replace("<GS>", "\u001d", StringComparison.OrdinalIgnoreCase)
            .Replace("[RS]", "\u001e", StringComparison.OrdinalIgnoreCase)
            .Replace("[GS]", "\u001d", StringComparison.OrdinalIgnoreCase)
            .Replace("{RS}", "\u001e", StringComparison.OrdinalIgnoreCase)
            .Replace("{GS}", "\u001d", StringComparison.OrdinalIgnoreCase)
            .Replace("♥", string.Empty, StringComparison.Ordinal)
            .Replace("\u0000", string.Empty, StringComparison.Ordinal);
    }

    private static int FindFirstDataIdentifier(string value)
    {
        var best = -1;
        foreach (var marker in new[] { "06", "17V", "18S" })
        {
            var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && (best < 0 || index < best))
            {
                best = index;
            }
        }

        return best;
    }

    private static Dictionary<string, string> ParseKeyValuePairs(string raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var segment in SegmentRegex().Split(raw))
        {
            var match = KeyValueRegex().Match(segment.Trim());
            if (!match.Success)
            {
                continue;
            }

            var key = NormalizeKey(match.Groups["key"].Value);
            var value = match.Groups["value"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static BarcodeParseResult ParseGs1(string normalizedInput, string raw)
    {
        var normalized = normalizedInput.Replace("\u001d", string.Empty, StringComparison.Ordinal);
        var serial = MatchGs1Value(normalized, "21");
        var product = MatchGs1Value(normalized, "240");

        return new BarcodeParseResult
        {
            Parsed = !string.IsNullOrWhiteSpace(serial) || !string.IsNullOrWhiteSpace(product),
            PartNumber = product,
            SerialNumber = serial,
            RawValue = raw
        };
    }

    private static string MatchGs1Value(string input, string applicationIdentifier)
    {
        var parenthesized = Regex.Match(
            input,
            $@"\({Regex.Escape(applicationIdentifier)}\)(?<value>.*?)(?=\(\d{{2,4}}\)|$)",
            RegexOptions.CultureInvariant);

        return parenthesized.Success
            ? parenthesized.Groups["value"].Value.Trim()
            : string.Empty;
    }

    private static string FindValue(
        Dictionary<string, string> dictionary,
        IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (dictionary.TryGetValue(NormalizeKey(key), out var value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string NormalizeKey(string value)
    {
        return new string(value
            .Where(character => char.IsLetterOrDigit(character))
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    [GeneratedRegex(@"[\r\n;|]+")]
    private static partial Regex SegmentRegex();

    [GeneratedRegex(@"^(?<key>[A-Za-z0-9/_-]+)\s*[:=]\s*(?<value>.+)$")]
    private static partial Regex KeyValueRegex();

    [GeneratedRegex(@"[\t,|]+")]
    private static partial Regex DelimitedPartsRegex();

    [GeneratedRegex(@"[\u0000-\u001f]")]
    private static partial Regex ControlCharacterRegex();

    [GeneratedRegex(@"^[A-Za-z0-9._/\-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PartNumberValueRegex();

    [GeneratedRegex(@"^[A-Za-z0-9._/\-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SerialValueRegex();
}
