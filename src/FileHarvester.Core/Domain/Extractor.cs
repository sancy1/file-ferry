using System.Text.RegularExpressions;

namespace FileHarvester.Core.Domain;

public static partial class Extractor
{
    private static readonly HashSet<string> FileExtensions = new(
        new[]
        {
            "7z", "avi", "bat", "bmp", "c", "cc", "cfg", "cpp", "cs", "css",
            "csv", "doc", "docx", "dll", "go", "gif", "gz", "h", "hpp", "html",
            "ics", "ini", "java", "jpeg", "jpg", "js", "json", "jsx", "kt",
            "log", "md", "mov", "mp3", "mp4", "pdf", "php", "png", "ppt",
            "pptx", "ps1", "py", "rb", "rs", "rtf", "scss", "sh", "sql",
            "svg", "tar", "tif", "tiff", "toml", "ts", "tsx", "txt", "wav",
            "webp", "xls", "xlsx", "xml", "yaml", "yml", "zip",
        },
        StringComparer.OrdinalIgnoreCase);

    public static ExtractionResult Extract(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var urls = FindCandidates(UrlRegex(), text, trimPunctuation: true);
        var emails = FindCandidates(EmailRegex(), text, trimPunctuation: true);
        var paths = new List<Candidate>(urls);
        paths.AddRange(FindCandidates(WindowsPathRegex(), text, trimPunctuation: true)
            .Where(candidate => !OverlapsAny(candidate, urls)));
        paths.AddRange(FindCandidates(UncPathRegex(), text, trimPunctuation: true)
            .Where(candidate => !OverlapsAny(candidate, urls)));
        var posixPaths = FindCandidates(PosixPathRegex(), text, trimPunctuation: true)
            .Where(candidate => !OverlapsAny(candidate, paths))
            .ToArray();
        paths.AddRange(posixPaths);
        var relativePaths = FindCandidates(RelativePathRegex(), text, trimPunctuation: true)
            .Where(candidate => !OverlapsAny(candidate, paths))
            .ToArray();
        paths.AddRange(relativePaths);
        var protectedSpans = urls.Concat(emails).Concat(paths).ToArray();
        paths.AddRange(FindCandidates(BareFilenameRegex(), text, trimPunctuation: true)
            .Where(candidate => !OverlapsAny(candidate, protectedSpans)
                && HasKnownExtension(candidate.Value)));

        var linkItems = Deduplicate(paths
            .OrderBy(candidate => candidate.StartIndex)
            .Select(candidate => new ExtractedItem(
                ExtractCategory.LinksAndPaths,
                candidate.Value,
                candidate.StartIndex,
                candidate.Length)),
            StringComparer.Ordinal);

        var emailItems = Deduplicate(
            emails.Where(candidate => IsReasonableEmail(candidate.Value))
                .OrderBy(candidate => candidate.StartIndex)
                .Select(candidate => new ExtractedItem(
                    ExtractCategory.Emails,
                    candidate.Value,
                    candidate.StartIndex,
                    candidate.Length)),
            StringComparer.OrdinalIgnoreCase);

        var phones = FindCandidates(PhoneRegex(), text, trimPunctuation: false)
            .Where(candidate => !OverlapsAny(candidate, protectedSpans)
                && !LooksLikeDate(candidate.Value)
                && !LooksLikeIpv4(candidate.Value))
            .Where(candidate =>
            {
                var digitCount = candidate.Value.Count(char.IsDigit);
                return digitCount is >= 7 and <= 15;
            })
            .OrderBy(candidate => candidate.StartIndex)
            .Select(candidate => new ExtractedItem(
                ExtractCategory.Phones,
                candidate.Value.Trim(),
                candidate.StartIndex,
                candidate.Length))
            .ToArray();

        return new ExtractionResult(linkItems, emailItems, phones);
    }

    private static bool IsReasonableEmail(string value)
    {
        var separator = value.IndexOf('@');
        if (separator <= 0 || separator == value.Length - 1)
        {
            return false;
        }

        var localPart = value[..separator];
        return localPart[0] != '.'
            && localPart[^1] != '.'
            && !localPart.Contains("..", StringComparison.Ordinal);
    }

    private static ExtractedItem[] Deduplicate(
        IEnumerable<ExtractedItem> items,
        StringComparer comparer)
    {
        var seen = new HashSet<string>(comparer);
        return items.Where(item => seen.Add(item.Value)).ToArray();
    }

    private static List<Candidate> FindCandidates(
        Regex regex,
        string text,
        bool trimPunctuation)
    {
        var candidates = new List<Candidate>();
        foreach (Match match in regex.Matches(text))
        {
            var start = match.Index;
            var length = match.Length;
            var value = match.Value;
            if (trimPunctuation)
            {
                (start, value) = TrimCandidate(start, value);
                length = value.Length;
            }
            if (value.Length == 0)
            {
                continue;
            }
            candidates.Add(new Candidate(start, length, value));
        }
        return candidates;
    }

    private static (int Start, string Value) TrimCandidate(int start, string value)
    {
        var leadingTrimmed = value.TrimStart('(', '[', '{', '"', '\'', '<');
        start += value.Length - leadingTrimmed.Length;
        value = leadingTrimmed;

        while (value.Length > 0 && value[^1] is '.' or ',' or ';')
        {
            value = value[..^1];
        }

        while (value.Length > 0 && value[^1] is ')' or ']' or '}')
        {
            var last = value[^1];
            var open = last switch
            {
                ')' => '(',
                ']' => '[',
                _ => '{',
            };
            if (value.Count(character => character == last)
                <= value.Count(character => character == open))
            {
                break;
            }
            value = value[..^1];
        }
        return (start, value);
    }

    private static bool OverlapsAny(Candidate candidate, IReadOnlyList<Candidate> spans) =>
        spans.Any(span => candidate.StartIndex < span.StartIndex + span.Length
            && span.StartIndex < candidate.StartIndex + candidate.Length);

    private static bool HasKnownExtension(string value)
    {
        var dot = value.LastIndexOf('.');
        return dot >= 0 && FileExtensions.Contains(value[(dot + 1)..]);
    }

    private static bool LooksLikeDate(string value) =>
        DateRegex().IsMatch(value.Trim());

    private static bool LooksLikeIpv4(string value) =>
        Ipv4Regex().IsMatch(value.Trim());

    private sealed record Candidate(int StartIndex, int Length, string Value);

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:https?|ftp|file)://[^\s<>""']+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:[A-Za-z]:[\\/])[^\s<>""|?*]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:\\\\|//)[A-Za-z0-9._-]+[\\/][^\s<>:""|?*]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex UncPathRegex();

    [GeneratedRegex(@"(?<![\p{L}\p{N}:])/(?!/)[^\s<>""']+",
        RegexOptions.CultureInvariant)]
    private static partial Regex PosixPathRegex();

    [GeneratedRegex(@"(?<![\p{L}\p{N}:])[\p{L}\p{N}_.-]+(?:[\\/][\p{L}\p{N}_.-]+)+\.(?:[A-Za-z0-9]{1,12})(?![\p{L}\p{N}])",
        RegexOptions.CultureInvariant)]
    private static partial Regex RelativePathRegex();

    [GeneratedRegex(@"(?<![\p{L}\p{N}_@.-])[\p{L}\p{N}_][\p{L}\p{N}_.-]*\.(?:[A-Za-z0-9]{1,12})(?![\p{L}\p{N}])",
        RegexOptions.CultureInvariant)]
    private static partial Regex BareFilenameRegex();

    [GeneratedRegex(@"(?<![\p{L}\p{N}._%+-])[A-Z0-9.!#$%&'*+/=?^_`{|}~-]+@(?:[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?\.)+[A-Z]{2,63}(?![\p{L}\p{N}-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:\+|00)?\(?\d(?:[\d \t().-]{4,28}\d)\)?(?![\p{L}\p{N}])",
        RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^\d{4}[-/.]\d{1,2}[-/.]\d{1,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"^(?:\d{1,3}\.){3}\d{1,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex Ipv4Regex();
}
