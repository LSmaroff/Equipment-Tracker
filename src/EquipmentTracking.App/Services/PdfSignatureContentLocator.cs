using System.Text;
using System.Text.RegularExpressions;

namespace EquipmentTracking.App.Services;

public static partial class PdfSignatureContentLocator
{
    public static IReadOnlyList<string> FindCandidateHexContents(
        string pdfText,
        string? preferredFieldName,
        bool preferredFieldOnly = false)
    {
        ArgumentNullException.ThrowIfNull(pdfText);

        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddCandidate(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return;
            }

            var normalized = WhitespaceRegex().Replace(hex, string.Empty);
            if (normalized.Length >= 2 && seen.Add(normalized))
            {
                candidates.Add(normalized);
            }
        }

        if (!string.IsNullOrWhiteSpace(preferredFieldName))
        {
            AddPreferredFieldCandidates(pdfText, preferredFieldName.Trim(), AddCandidate);
        }

        if (!preferredFieldOnly)
        {
            foreach (Match match in SignatureContentsRegex().Matches(pdfText).Cast<Match>().Reverse())
            {
                AddCandidate(match.Groups["hex"].Value);
            }
        }

        return candidates;
    }

    public static bool ContainsFieldName(string pdfText, string fieldName)
    {
        ArgumentNullException.ThrowIfNull(pdfText);
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return false;
        }

        foreach (Match match in ObjectRegex().Matches(pdfText))
        {
            if (FieldNamesMatch(TryReadFieldName(match.Groups["body"].Value), fieldName))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddPreferredFieldCandidates(
        string pdfText,
        string preferredFieldName,
        Action<string?> addCandidate)
    {
        var objects = new Dictionary<string, string>(StringComparer.Ordinal);
        var orderedObjects = ObjectRegex().Matches(pdfText).Cast<Match>().ToArray();

        foreach (var match in orderedObjects)
        {
            // The same object can appear more than once in an incrementally saved PDF.
            // Keeping the last body gives the newest object revision, which is where
            // Adobe normally writes the /V reference after a digital signature is added.
            objects[BuildObjectKey(match)] = match.Groups["body"].Value;
        }

        var parentReferences = BuildParentReferenceLookup(objects);
        foreach (var match in orderedObjects.Reverse())
        {
            var fieldBody = match.Groups["body"].Value;
            var fieldName = TryReadFieldName(fieldBody);
            if (!FieldNamesMatch(fieldName, preferredFieldName))
            {
                continue;
            }

            AddContentsFromFieldAndRelatedObjects(
                BuildObjectKey(match),
                objects,
                parentReferences,
                addCandidate,
                visited: new HashSet<string>(StringComparer.Ordinal));
        }
    }

    private static Dictionary<string, List<string>> BuildParentReferenceLookup(
        IReadOnlyDictionary<string, string> objects)
    {
        var childrenByParent = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var pair in objects)
        {
            foreach (Match parentReference in ParentReferenceRegex().Matches(pair.Value).Cast<Match>())
            {
                var parentKey = BuildReferenceKey(parentReference);
                if (!childrenByParent.TryGetValue(parentKey, out var children))
                {
                    children = new List<string>();
                    childrenByParent[parentKey] = children;
                }

                children.Add(pair.Key);
            }
        }

        return childrenByParent;
    }

    private static void AddContentsFromFieldAndRelatedObjects(
        string objectKey,
        IReadOnlyDictionary<string, string> objects,
        IReadOnlyDictionary<string, List<string>> parentReferences,
        Action<string?> addCandidate,
        HashSet<string> visited)
    {
        if (!visited.Add(objectKey) || !objects.TryGetValue(objectKey, out var body))
        {
            return;
        }

        AddContentsFromBody(body, addCandidate);
        AddContentsFromValueReferences(body, objects, addCandidate);

        foreach (var childKey in EnumerateKidReferenceKeys(body))
        {
            AddContentsFromFieldAndRelatedObjects(
                childKey,
                objects,
                parentReferences,
                addCandidate,
                visited);
        }

        if (!parentReferences.TryGetValue(objectKey, out var children))
        {
            return;
        }

        foreach (var childKey in children)
        {
            AddContentsFromFieldAndRelatedObjects(
                childKey,
                objects,
                parentReferences,
                addCandidate,
                visited);
        }
    }

    private static void AddContentsFromValueReferences(
        string body,
        IReadOnlyDictionary<string, string> objects,
        Action<string?> addCandidate)
    {
        foreach (Match valueReference in ValueReferenceRegex().Matches(body).Cast<Match>())
        {
            var key = BuildReferenceKey(valueReference);
            if (objects.TryGetValue(key, out var signatureBody))
            {
                AddContentsFromBody(signatureBody, addCandidate);
            }
        }
    }

    private static IEnumerable<string> EnumerateKidReferenceKeys(string body)
    {
        foreach (Match kidsMatch in KidArrayRegex().Matches(body).Cast<Match>())
        {
            foreach (Match referenceMatch in IndirectReferenceRegex().Matches(kidsMatch.Groups["refs"].Value).Cast<Match>())
            {
                yield return BuildReferenceKey(referenceMatch);
            }
        }
    }

    private static void AddContentsFromBody(string body, Action<string?> addCandidate)
    {
        foreach (Match contents in SignatureContentsRegex().Matches(body).Cast<Match>().Reverse())
        {
            addCandidate(contents.Groups["hex"].Value);
        }
    }

    private static bool FieldNamesMatch(string? actual, string expected)
    {
        var normalizedActual = NormalizeFieldName(actual);
        var normalizedExpected = NormalizeFieldName(expected);

        if (normalizedActual.Length == 0 || normalizedExpected.Length == 0)
        {
            return false;
        }

        return string.Equals(normalizedActual, normalizedExpected, StringComparison.OrdinalIgnoreCase) ||
               normalizedActual.EndsWith($".{normalizedExpected}", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFieldName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return WhitespaceRegex().Replace(value.Trim().Trim('\0'), " ");
    }

    private static string? TryReadFieldName(string objectBody)
    {
        var literalMatch = LiteralFieldNameRegex().Match(objectBody);
        if (literalMatch.Success)
        {
            return DecodePdfLiteralString(literalMatch.Groups["name"].Value);
        }

        var hexMatch = HexFieldNameRegex().Match(objectBody);
        if (!hexMatch.Success)
        {
            return null;
        }

        var hex = WhitespaceRegex().Replace(hexMatch.Groups["hex"].Value, string.Empty);
        if ((hex.Length & 1) == 1)
        {
            hex += "0";
        }

        try
        {
            return DecodePdfStringBytes(Convert.FromHexString(hex));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string DecodePdfLiteralString(string value)
    {
        var builder = new StringBuilder(value.Length);

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character != '\\' || index + 1 >= value.Length)
            {
                builder.Append(character);
                continue;
            }

            var escaped = value[++index];
            switch (escaped)
            {
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'b':
                    builder.Append('\b');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case '(':
                case ')':
                case '\\':
                    builder.Append(escaped);
                    break;
                case '\r':
                    if (index + 1 < value.Length && value[index + 1] == '\n')
                    {
                        index++;
                    }
                    break;
                case '\n':
                    break;
                default:
                    if (escaped is >= '0' and <= '7')
                    {
                        var octal = new StringBuilder(3).Append(escaped);
                        while (octal.Length < 3 &&
                               index + 1 < value.Length &&
                               value[index + 1] is >= '0' and <= '7')
                        {
                            octal.Append(value[++index]);
                        }

                        builder.Append((char)Convert.ToInt32(octal.ToString(), 8));
                    }
                    else
                    {
                        builder.Append(escaped);
                    }
                    break;
            }
        }

        var bytes = Encoding.Latin1.GetBytes(builder.ToString());
        return DecodePdfStringBytes(bytes);
    }

    private static string DecodePdfStringBytes(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2).TrimEnd('\0');
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2).TrimEnd('\0');
        }

        return Encoding.Latin1.GetString(bytes).TrimEnd('\0');
    }

    private static string BuildObjectKey(Match match)
    {
        return $"{match.Groups["number"].Value} {match.Groups["generation"].Value}";
    }

    private static string BuildReferenceKey(Match match)
    {
        return $"{match.Groups["number"].Value} {match.Groups["generation"].Value}";
    }

    [GeneratedRegex(
        @"(?ms)(?<number>\d+)\s+(?<generation>\d+)\s+obj\b(?<body>.*?)\bendobj\b",
        RegexOptions.CultureInvariant, 2000)]
    private static partial Regex ObjectRegex();

    [GeneratedRegex(
        @"/Contents\s*<(?<hex>[0-9A-Fa-f\s]+)>",
        RegexOptions.CultureInvariant, 2000)]
    private static partial Regex SignatureContentsRegex();

    [GeneratedRegex(
        @"/V\s+(?<number>\d+)\s+(?<generation>\d+)\s+R\b",
        RegexOptions.CultureInvariant, 2000)]
    private static partial Regex ValueReferenceRegex();

    [GeneratedRegex(
        @"/Kids\s*\[(?<refs>.*?)\]",
        RegexOptions.CultureInvariant | RegexOptions.Singleline, 2000)]
    private static partial Regex KidArrayRegex();

    [GeneratedRegex(
        @"(?<number>\d+)\s+(?<generation>\d+)\s+R\b",
        RegexOptions.CultureInvariant, 2000)]
    private static partial Regex IndirectReferenceRegex();

    [GeneratedRegex(
        @"/Parent\s+(?<number>\d+)\s+(?<generation>\d+)\s+R\b",
        RegexOptions.CultureInvariant, 2000)]
    private static partial Regex ParentReferenceRegex();

    [GeneratedRegex(
        @"/T\s*\((?<name>(?:\\.|[^\\)])*)\)",
        RegexOptions.CultureInvariant, 2000)]
    private static partial Regex LiteralFieldNameRegex();

    [GeneratedRegex(
        @"/T\s*<(?<hex>[0-9A-Fa-f\s]+)>",
        RegexOptions.CultureInvariant, 2000)]
    private static partial Regex HexFieldNameRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex WhitespaceRegex();
}
