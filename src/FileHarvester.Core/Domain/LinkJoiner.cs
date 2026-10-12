using System.Text.RegularExpressions;

namespace FileHarvester.Core.Domain;

public static partial class LinkJoiner
{
    private static readonly string[] LineSeparators = { "\r\n", "\n", "\r" };
    private static readonly char[] PathSeparators = { '/', '\\' };
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static LinkJoinerResult Join(
        IEnumerable<LinkJoinerSectionInput> sections,
        bool removeDuplicates = false)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var rows = new List<LinkJoinerRow>();
        var seen = removeDuplicates ? new HashSet<string>(PathComparer) : null;

        foreach (var section in sections)
        {
            ArgumentNullException.ThrowIfNull(section);
            var source = section.Paths ?? string.Empty;
            if (source.Length == 0)
            {
                continue;
            }
            var lines = source.Split(
                LineSeparators,
                StringSplitOptions.None);
            for (var index = 0; index < lines.Length; index++)
            {
                var input = lines[index].Trim();
                if (input.Length == 0)
                {
                    rows.Add(new LinkJoinerRow(
                        section.Name, index + 1, lines[index], null,
                        LinkJoinerRowKind.Blank, "Blank line."));
                    continue;
                }

                var transformed = Transform(section, input);
                if (transformed.Kind is LinkJoinerRowKind.Invalid)
                {
                    rows.Add(transformed with { SectionName = section.Name, LineNumber = index + 1, Input = lines[index] });
                    continue;
                }

                if (seen is not null && !seen.Add(transformed.Result!))
                {
                    rows.Add(new LinkJoinerRow(
                        section.Name, index + 1, lines[index], null,
                        LinkJoinerRowKind.Duplicate, "Duplicate result omitted by the selected option."));
                    continue;
                }

                rows.Add(transformed with
                {
                    SectionName = section.Name,
                    LineNumber = index + 1,
                    Input = lines[index],
                });
            }
        }

        return new LinkJoinerResult(rows);
    }

    private static LinkJoinerRow Transform(LinkJoinerSectionInput section, string input)
    {
        if (input.Any(char.IsControl))
        {
            return Invalid(input, "Path contains a control character.");
        }

        if (DriveRelativePathRegex().IsMatch(input))
        {
            return Invalid(input, "Drive-relative paths are ambiguous; use a drive-rooted absolute path.");
        }

        if (IsUri(input))
        {
            return new LinkJoinerRow(
                section.Name, 0, input, input, LinkJoinerRowKind.PassThrough,
                "URI passed through unchanged.");
        }

        if (IsAbsolutePath(input))
        {
            var absoluteBase = section.BaseLocation?.Trim() ?? string.Empty;
            if (absoluteBase.Length > 0 && IsAbsolutePath(absoluteBase))
            {
                var normalizedAbsoluteBase = NormalizeSeparators(absoluteBase, absoluteBase);
                var normalizedAbsoluteInput = NormalizeSeparators(input, absoluteBase);
                if (HasBasePrefix(normalizedAbsoluteInput, normalizedAbsoluteBase))
                {
                    return new LinkJoinerRow(
                        section.Name, 0, input, input, LinkJoinerRowKind.AlreadyPrefixed,
                        "Already contains this base; passed through unchanged.");
                }
            }

            return new LinkJoinerRow(
                section.Name, 0, input, input, LinkJoinerRowKind.PassThrough,
                "Absolute path passed through unchanged; it is not treated as inside the selected base.");
        }

        var baseLocation = section.BaseLocation?.Trim() ?? string.Empty;
        if (baseLocation.Length == 0 || !IsAbsolutePath(baseLocation))
        {
            return Invalid(input, "Enter an absolute local base location for relative paths.");
        }
        if (baseLocation.Any(char.IsControl) || IsUri(baseLocation))
        {
            return Invalid(input, "The base location must be an absolute local path.");
        }

        var normalizedBase = NormalizeSeparators(baseLocation, baseLocation);
        var normalizedInput = NormalizeSeparators(input, baseLocation);
        if (HasBasePrefix(normalizedInput, normalizedBase))
        {
            return new LinkJoinerRow(
                section.Name, 0, input, section.NormalizeSeparators ? normalizedInput : input,
                LinkJoinerRowKind.AlreadyPrefixed, "Entry already begins with this base.");
        }

        if (!TryResolveRelative(normalizedBase, normalizedInput, out var joined))
        {
            return Invalid(input, "Relative path traverses above the selected base.");
        }

        var result = section.NormalizeSeparators
            ? joined
            : JoinPreservingSeparators(baseLocation, input);
        return new LinkJoinerRow(section.Name, 0, input, result, LinkJoinerRowKind.Joined, null);
    }

    private static bool TryResolveRelative(string basePath, string relative, out string result)
    {
        result = string.Empty;
        var rootLength = GetRootLength(basePath);
        if (rootLength == 0)
        {
            return false;
        }

        var root = basePath[..rootLength];
        var baseSegments = SplitSegments(basePath[rootLength..]);
        var segments = new List<string>(baseSegments);
        foreach (var part in SplitSegments(relative))
        {
            if (part == ".")
            {
                continue;
            }
            if (part == "..")
            {
                if (segments.Count <= baseSegments.Length)
                {
                    return false;
                }
                segments.RemoveAt(segments.Count - 1);
                continue;
            }
            if (part.Contains(':'))
            {
                return false;
            }
            segments.Add(part);
        }

        var separator = GetPreferredSeparator(basePath);
        result = root;
        if (segments.Count > 0
            && !root.EndsWith('/') && !root.EndsWith('\\'))
        {
            result += separator;
        }
        result += string.Join(separator, segments);
        return true;
    }

    private static string JoinPreservingSeparators(string basePath, string relative)
    {
        if (HasBasePrefix(relative, basePath))
        {
            return relative;
        }

        var baseHasTrailingSeparator = basePath.EndsWith('/') || basePath.EndsWith('\\');
        var separator = baseHasTrailingSeparator
            ? string.Empty
            : basePath.Contains('\\')
                ? "\\"
                : basePath.Contains('/')
                    ? "/"
                    : relative.Contains('\\') ? "\\" : "/";
        return basePath + separator + relative;
    }

    private static bool HasBasePrefix(string path, string basePath)
    {
        var prefix = basePath.TrimEnd('/', '\\');
        if (prefix.Length == 0)
        {
            return path.StartsWith('/')
                || path.StartsWith("\\\\", StringComparison.Ordinal);
        }
        if (path.Length < prefix.Length
            || !path.StartsWith(prefix, IsWindowsPath(basePath)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal))
        {
            return false;
        }
        return path.Length == prefix.Length
            || path[prefix.Length] is '/' or '\\';
    }

    private static bool IsAbsolutePath(string value)
    {
        if (Regex.IsMatch(value, @"^[A-Za-z]:[\\/]", RegexOptions.CultureInvariant))
        {
            return true;
        }
        return value.StartsWith("\\\\", StringComparison.Ordinal)
            || value.StartsWith("//", StringComparison.Ordinal)
            || value.StartsWith('/');
    }

    private static bool IsUri(string value) =>
        UriSchemeRegex().IsMatch(value)
        && !Regex.IsMatch(value, @"^[A-Za-z]:[\\/]", RegexOptions.CultureInvariant);

    private static int GetRootLength(string path)
    {
        if (Regex.IsMatch(path, @"^[A-Za-z]:[\\/]", RegexOptions.CultureInvariant))
        {
            return 3;
        }
        if (path.StartsWith("\\\\", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal))
        {
            var pieces = SplitSegments(path[2..]);
            return pieces.Length < 2 ? 0 : 2 + pieces[0].Length + 1 + pieces[1].Length
                + (path.Length > 2 + pieces[0].Length + 1 + pieces[1].Length ? 1 : 0);
        }
        return path.StartsWith('/') ? 1 : 0;
    }

    private static string[] SplitSegments(string value) =>
        value.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);

    private static string NormalizeSeparators(string value, string? basePath = null)
    {
        var separator = basePath is null
            ? Path.DirectorySeparatorChar
            : GetPreferredSeparator(basePath);
        return value.Replace('\\', separator).Replace('/', separator);
    }

    private static bool IsWindowsPath(string path) =>
        Regex.IsMatch(path, @"^[A-Za-z]:", RegexOptions.CultureInvariant)
        || path.StartsWith("\\\\", StringComparison.Ordinal)
        || path.StartsWith("//", StringComparison.Ordinal);

    private static char GetPreferredSeparator(string path) =>
        IsWindowsPath(path) ? '\\' : '/';

    private static LinkJoinerRow Invalid(string input, string message) =>
        new(string.Empty, 0, input, null, LinkJoinerRowKind.Invalid, message);

    [GeneratedRegex(@"^[A-Za-z]:[^\\/]", RegexOptions.CultureInvariant)]
    private static partial Regex DriveRelativePathRegex();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.-]*:", RegexOptions.CultureInvariant)]
    private static partial Regex UriSchemeRegex();
}
