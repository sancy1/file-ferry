// ============================================================================
// FILE:     src\FileHarvester.Core/Platform/RecursiveWalkSearch.cs
// PURPOSE:  Universal recursive filesystem walker. Matches by exact name,
//           base name, or base-name prefix — all in one pass. No mode
//           toggle: one walk finds every plausible match.
// RELATION: Used by WindowsPathSearch. Owns skip-list logic.
// ============================================================================

using FileHarvester.Core.Domain;

namespace FileHarvester.Core.Platform;

public static class RecursiveWalkSearch
{
    private const int ProgressInterval = 100;

    private static readonly char[] PathSeparators = { '\\', '/' };

    public static Task<IReadOnlyList<string>> WalkAsync(
        string fileName,
        IReadOnlyList<string> roots,
        SearchOptions options,
        IProgress<SearchProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(options);

        return Task.Run(
            () => (IReadOnlyList<string>)Walk(fileName, roots, options, progress, cancellationToken),
            cancellationToken);
    }

    private static List<string> Walk(
        string fileName,
        IReadOnlyList<string> roots,
        SearchOptions options,
        IProgress<SearchProgress>? progress,
        CancellationToken cancellationToken)
    {
        var matches = new List<string>();
        var directoriesScanned = 0;
        var stack = new Stack<string>(roots.Reverse());

        var comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var skipList = options.EffectiveSkipList;

        // Pre-compute query pieces so the inner loop is fast.
        var query = fileName;
        var queryBase = Path.GetFileNameWithoutExtension(query);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (matches.Count >= options.MaxResults)
            {
                break;
            }

            var current = stack.Pop();
            directoriesScanned++;

            if (ShouldSkip(current, skipList, comparison))
            {
                continue;
            }

            IEnumerable<string> filesInDir;
            try
            {
                filesInDir = Directory.EnumerateFiles(current);
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            catch (IOException) { continue; }

            foreach (var filePath in filesInDir)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (matches.Count >= options.MaxResults)
                {
                    break;
                }

                if (Matches(filePath, query, queryBase, comparison))
                {
                    matches.Add(filePath);
                }
            }

            IEnumerable<string> subdirs;
            try
            {
                subdirs = Directory.EnumerateDirectories(current);
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            catch (IOException) { continue; }

            foreach (var subdir in subdirs)
            {
                if (!ShouldSkip(subdir, skipList, comparison))
                {
                    stack.Push(subdir);
                }
            }

            if (progress is not null && directoriesScanned % ProgressInterval == 0)
            {
                progress.Report(new SearchProgress(
                    Tier: SearchTier.RecursiveWalk,
                    DirectoriesScanned: directoriesScanned,
                    MatchesFound: matches.Count));
            }
        }

        progress?.Report(new SearchProgress(
            Tier: SearchTier.RecursiveWalk,
            DirectoriesScanned: directoriesScanned,
            MatchesFound: matches.Count));

        return matches;
    }

    /// <summary>
    /// Universal matching. A file matches if ANY of:
    ///   1. Its full name equals the query ("README.md" = "README.md").
    ///   2. Its base name equals the query base ("README" = "README").
    ///   3. Its base name starts with the query base ("README" matches "README-old").
    /// This makes search work regardless of whether the user typed the
    /// extension.
    /// </summary>
    private static bool Matches(
        string filePath,
        string query,
        string queryBase,
        StringComparison comparison)
    {
        var fileName = Path.GetFileName(filePath);
        var fileBase = Path.GetFileNameWithoutExtension(filePath);

        // 1. Exact full-name
        if (fileName.Equals(query, comparison))
        {
            return true;
        }

        // 2. Exact base-name (query = "README", file = "README.md")
        if (fileBase.Equals(queryBase, comparison))
        {
            return true;
        }

        // 3. Prefix base-name (query = "READ", file = "README.md")
        if (queryBase.Length >= 2 && fileBase.StartsWith(queryBase, comparison))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a directory should be skipped. Each skip entry is matched
    /// as a complete path segment (or multi-segment tail) rather than a
    /// substring.
    /// </summary>
    private static bool ShouldSkip(
        string path,
        IReadOnlyList<string> skipList,
        StringComparison comparison)
    {
        foreach (var skip in skipList)
        {
            if (string.IsNullOrWhiteSpace(skip))
            {
                continue;
            }

            // Absolute path prefix (e.g. "C:\Windows")
            if (skip.Length >= 2 && skip[1] == ':')
            {
                if (path.StartsWith(skip, comparison))
                {
                    return true;
                }
                continue;
            }

            // Segment tail match: normalise both to "/" and check for
            // the segment tail anywhere in the path.
            var normalizedPath = path.Replace('\\', '/').TrimEnd('/');
            var normalizedSkip = skip.Replace('\\', '/').Trim('/');

            if (normalizedSkip.Length == 0)
            {
                continue;
            }

            // Match either "/segment/" mid-path or "/segment" at end.
            if (normalizedPath.EndsWith("/" + normalizedSkip, comparison)
                || normalizedPath.Contains("/" + normalizedSkip + "/", comparison))
            {
                return true;
            }
        }

        return false;
    }
}
