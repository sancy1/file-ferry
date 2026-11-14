// ============================================================================
// FILE:     src/ImageTextify.Core/Diagnostics/ImageTextifyRedaction.cs
// PURPOSE:  The single place in ImageTextify that decides what is
//           safe to log. Replaces the user segment of an absolute
//           path with a literal marker and strips query strings and
//           user-info from a URI. Pure; no state; never throws.
// RELATION: Consumed by every ImageTextify log call site and by any
//           diagnostic surface that would otherwise print a path or
//           a URI.
// ============================================================================

namespace ImageTextify.Core.Diagnostics;

/// <summary>
/// Redaction helpers for ImageTextify diagnostic output.
/// </summary>
/// <remarks>
/// Every ImageTextify log line that would otherwise contain a full
/// path or a URI passes through this helper first. The helper is pure
/// and deterministic. It never throws; a null or empty input returns
/// the empty string.
/// </remarks>
public static class ImageTextifyRedaction
{
    private const string UserSegmentWindows = "\\Users\\";
    private const string UserSegmentPosix = "/home/";
    private const string UserSegmentMac = "/Users/";
    private const string UserMarker = "<user>";

    // Reused across calls so a hot log path does not allocate a
    // fresh char array on every invocation (CA1861).
    private static readonly char[] PathSeparators = new[] { '\\', '/' };

    /// <summary>
    /// Replaces the user-name segment of an absolute path with a
    /// literal marker, so a log line cannot leak the account name.
    /// The rest of the path is preserved.
    /// </summary>
    /// <param name="fullPath">
    /// The absolute path to redact. Null, empty, or whitespace returns
    /// the empty string.
    /// </param>
    /// <returns>The redacted path, or the empty string.</returns>
    public static string RedactPath(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return string.Empty;
        }

        var value = fullPath;

        value = ReplaceFirstSegment(value, UserSegmentWindows);
        value = ReplaceFirstSegment(value, UserSegmentPosix);
        value = ReplaceFirstSegment(value, UserSegmentMac);

        return value;
    }

    /// <summary>
    /// Strips the query string and any user-info segment from a URI so
    /// a signed URL, a pre-signed download link, or a token in the
    /// query cannot reach a log line.
    /// </summary>
    /// <param name="uri">
    /// The URI to redact. Null, empty, or whitespace returns the empty
    /// string.
    /// </param>
    /// <returns>The redacted URI, or the empty string.</returns>
    public static string RedactUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return string.Empty;
        }

        var queryIndex = uri.IndexOf('?');
        var withoutQuery = queryIndex >= 0 ? uri.Substring(0, queryIndex) : uri;

        var schemeEnd = withoutQuery.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
        {
            return withoutQuery;
        }

        var authorityStart = schemeEnd + 3;
        var atIndex = withoutQuery.IndexOf('@', authorityStart);
        if (atIndex < 0)
        {
            return withoutQuery;
        }

        var scheme = withoutQuery.Substring(0, authorityStart);
        var host = withoutQuery.Substring(atIndex + 1);
        return scheme + host;
    }

    private static string ReplaceFirstSegment(string value, string marker)
    {
        var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return value;
        }

        var afterMarker = index + marker.Length;
        var nextSeparator = value.IndexOfAny(PathSeparators, afterMarker);
        if (nextSeparator < 0)
        {
            return value;
        }

        var prefix = value.Substring(0, afterMarker);
        var suffix = value.Substring(nextSeparator);
        return prefix + UserMarker + suffix;
    }
}
