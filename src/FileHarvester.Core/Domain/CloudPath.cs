// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/CloudPath.cs
// PURPOSE:  Lexical, host-OS-independent path helpers for URI-shaped and
//           local paths. Segments on both / and \ for Windows-style
//           input, on / for POSIX-style input, and recognizes drive
//           letters and UNC prefixes independently of the host OS.
//           Does not call Path.GetFileName, Path.GetDirectoryName,
//           Path.GetFullPath, or Path.IsPathRooted.
// RELATION: Consumed by DelimiterTemplate, ZipFormatWriter,
//           OutputOptionsViewModel, and by cloud provider adapters
//           whenever a URI-shaped path must be split or joined.
//           Uses PathScheme.Parse only to detect a recognized scheme.
// ============================================================================

using System.Text;

namespace FileHarvester.Core.Domain;

/// <summary>
/// Lexical path helpers for both URI-shaped cloud paths and local
/// filesystem paths. All methods are pure, static, and produce the
/// same result on every host OS.
/// </summary>
/// <remarks>
/// <para>
/// Classification is lexical: a path is URI-shaped if it has a
/// recognized scheme (file, s3, gs, gdrive, azure, dropbox);
/// a Windows drive-letter path if it begins with a letter and a colon;
/// a UNC path if it begins with two separators; a POSIX path if it
/// begins with a single forward slash; otherwise a relative path.
/// </para>
/// <para>
/// Null, empty, and whitespace-only inputs are treated as the empty
/// string. No method throws for such inputs. Whitespace is trimmed
/// before classification.
/// </para>
/// </remarks>
public static class CloudPath
{
    private const char ForwardSlash = (char)47;
    private const char BackSlash = (char)92;

    /// <summary>
    /// Returns the last path segment. For URI-shaped paths, the segment
    /// is taken from the part after the scheme separator. For local paths,
    /// both / and \ are treated as separators. Returns the empty string
    /// when there is no segment.
    /// </summary>
    /// <param name="path">The path. Null, empty, and whitespace return empty.</param>
    /// <returns>The last segment, or empty.</returns>
    public static string GetFileName(string? path)
    {
        var trimmed = Trim(path);
        if (trimmed.Length == 0) return string.Empty;

        if (IsUriShaped(trimmed, out var rest))
        {
            return LastSegment(rest);
        }

        return LastSegment(trimmed);
    }

    /// <summary>
    /// Returns the last path segment with its file extension removed.
    /// For URI-shaped paths, the segment is taken from the part after the
    /// scheme separator. For local paths, both / and \ are treated as
    /// separators. Returns the empty string when there is no segment.
    /// A leading dot is treated as part of a hidden file name, not as
    /// an extension: ".gitignore" returns ".gitignore".
    /// </summary>
    /// <param name="path">The path. Null, empty, and whitespace return empty.</param>
    /// <returns>The last segment without its extension, or empty.</returns>
    public static string GetFileNameWithoutExtension(string? path)
    {
        var fileName = GetFileName(path);
        if (fileName.Length == 0) return string.Empty;

        var lastDot = fileName.LastIndexOf((char)46);
        if (lastDot <= 0) return fileName;

        return fileName.Substring(0, lastDot);
    }

    /// <summary>
    /// Returns the directory portion. For URI-shaped paths, the scheme
    /// separator and authority are preserved. For local paths, both /
    /// and \ are treated as separators. Returns empty when there is no
    /// directory portion.
    /// </summary>
    /// <param name="path">The path. Null, empty, and whitespace return empty.</param>
    /// <returns>The directory portion, or empty.</returns>
    public static string GetDirectoryName(string? path)
    {
        var trimmed = Trim(path);
        if (trimmed.Length == 0) return string.Empty;

        if (IsUriShaped(trimmed, out _))
        {
            return UriDirectory(trimmed);
        }

        return LocalDirectory(trimmed);
    }

    /// <summary>
    /// Joins a base path and a relative path with a single separator,
    /// preserving the scheme separator and authority for URI-shaped
    /// bases. If the relative path is an absolute local path, the
    /// absolute path wins and is returned unchanged.
    /// </summary>
    /// <param name="basePath">The base. Null and whitespace mean "use relative".</param>
    /// <param name="relativePath">The relative part. Null and whitespace mean "use base".</param>
    /// <returns>The combined path, or the non-empty side when one is empty.</returns>
    public static string Combine(string? basePath, string? relativePath)
    {
        var b = Trim(basePath);
        var r = Trim(relativePath);

        if (b.Length == 0) return r;
        if (r.Length == 0) return b;

        if (IsUriShaped(b, out _))
        {
            return UriCombine(b, r);
        }

        if (IsAbsoluteLocal(r))
        {
            return r;
        }

        return LocalCombine(b, r);
    }

    /// <summary>
    /// Ensures a single trailing separator. URI-shaped paths receive
    /// a forward slash; drive-letter and UNC paths receive a backslash;
    /// POSIX paths receive a forward slash.
    /// </summary>
    /// <param name="path">The path. Null, empty, and whitespace return empty.</param>
    /// <returns>The path with one trailing separator, or empty.</returns>
    public static string EnsureTrailingSlash(string? path)
    {
        var trimmed = Trim(path);
        if (trimmed.Length == 0) return string.Empty;

        var last = trimmed[trimmed.Length - 1];
        var alreadyHas = last == ForwardSlash || last == BackSlash;
        if (alreadyHas) return trimmed;

        if (IsUriShaped(trimmed, out _) || trimmed[0] == ForwardSlash)
        {
            return trimmed + ForwardSlash;
        }

        return trimmed + BackSlash;
    }

    /// <summary>
    /// True when the path ends with a separator and therefore denotes
    /// a directory (or a directory-like URI prefix). Null, empty, and
    /// whitespace return false.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>True if the path is directory-shaped.</returns>
    public static bool IsDirectoryShaped(string? path)
    {
        var trimmed = Trim(path);
        if (trimmed.Length == 0) return false;
        var last = trimmed[trimmed.Length - 1];
        return last == ForwardSlash || last == BackSlash;
    }

    // ------------------------------------------------------------------
    // Private helpers
    // ------------------------------------------------------------------

    private static string Trim(string? s) => (s ?? string.Empty).Trim();

    private static bool IsUriShaped(string path, out string rest)
    {
        var info = PathScheme.Parse(path);
        if (info.Kind != PathSchemeKind.Local)
        {
            rest = info.Rest;
            return true;
        }
        rest = string.Empty;
        return false;
    }

    private static bool IsAbsoluteLocal(string path)
    {
        if (path.Length == 0) return false;

        if (path.Length >= 2 && path[1] == (char)58)
        {
            var c = path[0];
            var isLetter = (c >= (char)65 && c <= (char)90) ||
                           (c >= (char)97 && c <= (char)122);
            if (isLetter) return true;
        }

        if (path.Length >= 2 &&
            (path[0] == BackSlash || path[0] == ForwardSlash) &&
            (path[1] == BackSlash || path[1] == ForwardSlash))
        {
            return true;
        }

        if (path[0] == ForwardSlash) return true;

        return false;
    }

    private static string LastSegment(string path)
    {
        if (path.Length > 0 && (path[path.Length - 1] == ForwardSlash || path[path.Length - 1] == BackSlash)) return string.Empty;
        var end = path.Length;
        while (end > 0 && (path[end - 1] == ForwardSlash || path[end - 1] == BackSlash)) end--;
        if (end == 0) return string.Empty;
        var start = end;
        while (start > 0 && path[start - 1] != ForwardSlash && path[start - 1] != BackSlash) start--;
        return path.Substring(start, end - start);
    }

    private static string LocalDirectory(string path)
    {
        var end = path.Length;
        while (end > 0 && (path[end - 1] == ForwardSlash || path[end - 1] == BackSlash)) end--;
        if (end == 0) return string.Empty;

        var lastSep = -1;
        for (var i = 0; i < end; i++)
        {
            if (path[i] == ForwardSlash || path[i] == BackSlash) lastSep = i;
        }

        if (lastSep < 0) return string.Empty;

        if (lastSep == 0) return path.Substring(0, 1);

        if (lastSep == 2 && path.Length >= 2 && path[1] == (char)58)
        {
            return path.Substring(0, 2);
        }

        return path.Substring(0, lastSep);
    }

    private static string UriDirectory(string path)
    {
        var sep = path.IndexOf("://", StringComparison.Ordinal);
        if (sep < 0) return string.Empty;

        var afterScheme = sep + 3;

        var lastSlash = -1;
        for (var i = afterScheme; i < path.Length; i++)
        {
            if (path[i] == ForwardSlash) lastSlash = i;
        }

        if (lastSlash < 0) return string.Empty;

        var end = lastSlash;
        while (end > afterScheme && path[end - 1] == ForwardSlash) end--;

        if (end == afterScheme)
        {
            var sb0 = new StringBuilder();
            sb0.Append(path, 0, afterScheme);
            return sb0.ToString();
        }

        return path.Substring(0, end);
    }

    private static string UriCombine(string b, string r)
    {
        var sep = b.IndexOf("://", StringComparison.Ordinal);
        if (sep < 0) return b;

        var afterScheme = sep + 3;

        var bEnd = b.Length;
        while (bEnd > afterScheme && b[bEnd - 1] == ForwardSlash) bEnd--;

        var rStart = 0;
        while (rStart < r.Length && r[rStart] == ForwardSlash) rStart++;

        var sb = new StringBuilder();
        sb.Append(b, 0, bEnd);
        sb.Append(ForwardSlash);
        sb.Append(r, rStart, r.Length - rStart);
        return sb.ToString();
    }

    private static string LocalCombine(string b, string r)
    {
        var bEnd = b.Length;
        while (bEnd > 0 && (b[bEnd - 1] == ForwardSlash || b[bEnd - 1] == BackSlash)) bEnd--;

        var rStart = 0;
        while (rStart < r.Length && (r[rStart] == ForwardSlash || r[rStart] == BackSlash)) rStart++;

        var sep = b.Contains(BackSlash) || !b.Contains(ForwardSlash)
            ? BackSlash
            : ForwardSlash;

        var sb = new StringBuilder();
        sb.Append(b, 0, bEnd);
        sb.Append(sep);
        sb.Append(r, rStart, r.Length - rStart);
        return sb.ToString();
    }
}
