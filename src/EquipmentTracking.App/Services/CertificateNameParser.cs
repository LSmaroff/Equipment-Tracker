using System.Text.RegularExpressions;
using EquipmentTracking.App.Models;

namespace EquipmentTracking.App.Services;

public sealed partial class CertificateNameParser
{
    private static readonly string[] KnownRanks = RankCatalog.Values
        .Where(value => !string.Equals(value, "Civilian", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(value, "Contractor", StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(value => value.Length)
        .ToArray();

    public CustomerIdentity Parse(string? simpleName, string? subject)
    {
        var identity = ParseDistinguishedName(subject);

        if (!identity.HasUsableName)
        {
            identity = ParseDisplayName(simpleName);
        }

        if (!identity.HasUsableName)
        {
            identity = ParseDisplayName(ExtractCommonName(subject));
        }

        identity.Rank = FirstNonEmpty(
            identity.Rank,
            ExtractRank(subject),
            ExtractRank(simpleName),
            ExtractRank(ExtractCommonName(subject)));
        identity.OriginalName = FirstNonEmpty(simpleName, ExtractCommonName(subject), subject);
        return identity;
    }

    private static CustomerIdentity ParseDistinguishedName(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return new CustomerIdentity();
        }

        return new CustomerIdentity
        {
            Rank = ExtractRank(subject),
            FirstName = NormalizeNamePart(MatchDnValue(subject, "G", "GN", "GIVENNAME")),
            MiddleInitial = NormalizeMiddle(MatchDnValue(subject, "I", "INITIALS")),
            LastName = NormalizeNamePart(MatchDnValue(subject, "SN", "SURNAME"))
        };
    }

    private static CustomerIdentity ParseDisplayName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new CustomerIdentity();
        }

        var cleaned = RemoveLeadingRank(name.Trim().Trim('"'), out var rank);

        if (cleaned.Contains(','))
        {
            var commaParts = cleaned.Split(',', 2, StringSplitOptions.TrimEntries);
            var remainder = WordsRegex().Split(commaParts[1])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            return new CustomerIdentity
            {
                Rank = rank,
                FirstName = remainder.Length > 0 ? NormalizeNamePart(remainder[0]) : string.Empty,
                MiddleInitial = remainder.Length > 1 ? NormalizeMiddle(remainder[1]) : string.Empty,
                LastName = NormalizeNamePart(commaParts[0])
            };
        }

        var dotParts = cleaned.Split(
                '.',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !DigitsOnlyRegex().IsMatch(part))
            .ToArray();

        if (dotParts.Length >= 2)
        {
            return new CustomerIdentity
            {
                Rank = rank,
                LastName = NormalizeNamePart(dotParts[0]),
                FirstName = NormalizeNamePart(dotParts[1]),
                MiddleInitial = dotParts.Length >= 3 ? NormalizeMiddle(dotParts[2]) : string.Empty
            };
        }

        var wordParts = WordsRegex().Split(cleaned)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        if (wordParts.Length >= 2)
        {
            return new CustomerIdentity
            {
                Rank = rank,
                FirstName = NormalizeNamePart(wordParts[0]),
                MiddleInitial = wordParts.Length > 2 ? NormalizeMiddle(wordParts[1]) : string.Empty,
                LastName = NormalizeNamePart(wordParts[^1])
            };
        }

        return new CustomerIdentity { Rank = rank };
    }

    private static string ExtractRank(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var title = MatchDnValue(value, "T", "TITLE");
        var candidates = new[] { title }
            .Concat(Regex.Matches(value, "(?:^|,\\s*)OU\\s*=\\s*(?:\\\"(?<value>[^\\\"]+)\\\"|(?<value>[^,]+))", RegexOptions.IgnoreCase)
                .Cast<Match>()
                .Select(match => match.Groups["value"].Value))
            .Append(value);

        foreach (var candidate in candidates)
        {
            foreach (var rank in KnownRanks)
            {
                if (Regex.IsMatch(
                    candidate ?? string.Empty,
                    $@"(?<![A-Za-z]){Regex.Escape(rank)}(?![A-Za-z])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    return RankCatalog.Normalize(rank);
                }
            }
        }

        return string.Empty;
    }

    private static string RemoveLeadingRank(string value, out string rank)
    {
        rank = string.Empty;
        foreach (var knownRank in KnownRanks)
        {
            var match = Regex.Match(
                value,
                $@"^\s*{Regex.Escape(knownRank)}(?:\s+|[.,-]+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                continue;
            }

            rank = RankCatalog.Normalize(knownRank);
            return value[match.Length..].Trim();
        }

        return value;
    }

    private static string MatchDnValue(string subject, params string[] keys)
    {
        foreach (var key in keys)
        {
            var match = Regex.Match(
                subject,
                $"(?:^|,\\s*){Regex.Escape(key)}\\s*=\\s*(?:\\\"(?<value>[^\\\"]+)\\\"|(?<value>[^,]+))",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (match.Success)
            {
                return match.Groups["value"].Value.Trim();
            }
        }

        return string.Empty;
    }

    private static string ExtractCommonName(string? subject) =>
        string.IsNullOrWhiteSpace(subject) ? string.Empty : MatchDnValue(subject, "CN");

    private static string NormalizeNamePart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lower = value.Trim().ToLowerInvariant();
        return string.Join(
            '-',
            lower.Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static string NormalizeMiddle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var letter = value.FirstOrDefault(char.IsLetter);
        return letter == default ? string.Empty : char.ToUpperInvariant(letter).ToString();
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    [GeneratedRegex(@"\s+")]
    private static partial Regex WordsRegex();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex DigitsOnlyRegex();
}
