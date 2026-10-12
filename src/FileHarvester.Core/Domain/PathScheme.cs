// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/PathScheme.cs
// PURPOSE:  URI-scheme detection and parsing for file identifiers.
//           Today a path is a Windows path; in v1.1 it may be a cloud
//           URI (s3://, gs://, gdrive://, azure://, dropbox://) or an
//           explicit file:// URI. This class recognizes the scheme
//           without assuming anything about the rest of the path.
//           A cloud path may carry an optional instance segment:
//           scheme://[instance]/rest. The brackets select one stored
//           connection; no brackets means the scheme's single
//           connection, if exactly one exists.
// RELATION: Consumed by CloudPath, CloudFileResolver, CloudContentReader,
//           CloudOutputSink, CloudPathSearchService, and by any code
//           that branches on whether a path is local or remote.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// A recognized URI scheme, or Local for a plain filesystem path.
/// </summary>
/// <remarks>
/// <para>
/// This enum is deliberately closed. A path with an unrecognized scheme
/// (http://, ftp://, xyz://) is treated as Local. Cloud providers are
/// a fixed set; adding one is a code change, not a configuration.
/// </para>
/// <para>
/// <see cref="Local"/> is the zero value, so an uninitialized variable
/// or a default(PathSchemeKind) is always the safe choice.
/// </para>
/// </remarks>
public enum PathSchemeKind
{
    /// <summary>No recognized scheme. A plain filesystem path.</summary>
    Local = 0,

    /// <summary>Explicit local filesystem URI: file:///C:/path.</summary>
    File = 1,

    /// <summary>AWS S3 or S3-compatible object store: s3://bucket/key.</summary>
    S3 = 2,

    /// <summary>Google Cloud Storage: gs://bucket/key.</summary>
    Gcs = 3,

    /// <summary>Google Drive: gdrive://file/&lt;id&gt;.</summary>
    GoogleDrive = 4,

    /// <summary>Azure Blob Storage: azure://container/blob.</summary>
    AzureBlob = 5,

    /// <summary>Dropbox: dropbox:///path.</summary>
    Dropbox = 6,

    /// <summary>Microsoft OneDrive: onedrive://&lt;item-id&gt;.</summary>
    OneDrive = 7,
}

/// <summary>
/// The result of parsing a path into a scheme and the remainder.
/// </summary>
/// <param name="Kind">
/// The recognized scheme kind. Local when no recognized scheme is
/// present.
/// </param>
/// <param name="Scheme">
/// The lowercase scheme prefix without the "://" separator, or the
/// empty string for Local.
/// </param>
/// <param name="Rest">
/// Everything after the optional instance segment, or the whole
/// trimmed input for Local. The bracketed instance, when present,
/// is stripped from the front of this value.
/// </param>
/// <param name="Instance">
/// The bracketed instance segment, or null when the path carries
/// no instance. See PathScheme.Parse for the exact syntax rules.
/// </param>
public sealed record PathSchemeInfo(
    PathSchemeKind Kind,
    string Scheme,
    string Rest,
    string? Instance);

/// <summary>
/// Recognizes and parses URI schemes in file identifiers.
/// </summary>
/// <remarks>
/// <para>
/// All methods are static and pure. No I/O. No configuration. The
/// recognized schemes are the six reserved for v1.1 cloud integration:
/// file, s3, gs, gdrive, azure, dropbox.
/// </para>
/// <para>
/// A scheme is recognized only when followed by "://". A single slash
/// (s3:/bucket) or no slash (s3:bucket) is not a scheme - the whole
/// string is treated as a Local path.
/// </para>
/// </remarks>
public static class PathScheme
{
    private const string Separator = "://";

    /// <summary>
    /// Parses a path into a <see cref="PathSchemeInfo"/>.
    /// </summary>
    /// <param name="path">
    /// The input. May be null, empty, whitespace, a Windows path, a
    /// POSIX path, a UNC path, or a URI-shaped cloud path.
    /// </param>
    /// <returns>
    /// A <see cref="PathSchemeInfo"/>. Never null. For unrecognized
    /// inputs the Kind is Local, the Scheme is empty, and the Rest is
    /// the trimmed input.
    /// </returns>
    /// <exception cref="FormatException">
    /// A cloud path carries a bracketed instance that is malformed: an
    /// unclosed bracket, an empty instance, or a name containing a
    /// closing bracket or a colon. The malformed path is rejected; it
    /// never falls back to the no-instance behavior.
    /// </exception>
    public static PathSchemeInfo Parse(string? path)
    {
        var trimmed = (path ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            return new PathSchemeInfo(PathSchemeKind.Local, string.Empty, string.Empty, null);
        }

        var separatorIndex = trimmed.IndexOf(Separator, StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            return new PathSchemeInfo(PathSchemeKind.Local, string.Empty, trimmed, null);
        }

        var prefix = trimmed.Substring(0, separatorIndex);
        var rest = trimmed.Substring(separatorIndex + Separator.Length);

        var kind = GetKind(prefix);
        if (kind == PathSchemeKind.Local)
        {
            return new PathSchemeInfo(PathSchemeKind.Local, string.Empty, trimmed, null);
        }

        // Extract an optional bracketed instance: scheme://[instance]/rest.
        // No bracket -> Instance is null and Rest is unchanged. A malformed
        // bracket throws FormatException; it never falls back.
        string? instance = null;
        var effectiveRest = rest;

        if (rest.Length > 0 && rest[0] == (char)91)
        {
            var close = rest.IndexOf((char)93);
            if (close < 0)
            {
                throw new FormatException(
                    "Cloud path has an unclosed instance bracket: " + trimmed);
            }

            instance = rest.Substring(1, close - 1);

            if (!IsValidInstanceName(instance))
            {
                throw new FormatException(
                    "Cloud path has an invalid instance name: " + trimmed);
            }

            effectiveRest = rest.Substring(close + 1);
            if (effectiveRest.StartsWith('/'))
            {
                effectiveRest = effectiveRest.Substring(1);
            }
        }

        return new PathSchemeInfo(kind, prefix.ToLowerInvariant(), effectiveRest, instance);
    }

    /// <summary>
    /// True when the path has no recognized scheme (a local filesystem
    /// path, a Windows drive path, a POSIX path, or a UNC path). A
    /// malformed bracketed cloud path is treated as local, so it routes
    /// to the local resolver and is reported there, not thrown during
    /// routing.
    /// </summary>
    /// <param name="path">The path. Null and empty are Local.</param>
    /// <returns>True if local.</returns>
    public static bool IsLocal(string? path)
    {
        try
        {
            return Parse(path).Kind == PathSchemeKind.Local;
        }
        catch (FormatException)
        {
            return true;
        }
    }

    /// <summary>
    /// True when the instance name is valid: non-null, non-empty, and
    /// free of the characters that would make the path ambiguous (the
    /// closing bracket and the colon).
    /// </summary>
    /// <param name="instance">The candidate instance name.</param>
    /// <returns>True when the name is usable.</returns>
    public static bool IsValidInstanceName(string? instance)
    {
        if (string.IsNullOrWhiteSpace(instance)) return false;
        foreach (var c in instance)
        {
            if (c == (char)93 || c == (char)58) return false;
        }
        return true;
    }

    /// <summary>
    /// True when the path has a recognized cloud scheme (s3, gs, gdrive,
    /// azure, or dropbox). file:// is NOT a cloud path - it is local.
    /// A malformed bracketed cloud path is not cloud, so it is not
    /// dispatched to the cloud resolver.
    /// </summary>
    /// <param name="path">The path. Null and empty are not cloud.</param>
    /// <returns>True if the path is a cloud path.</returns>
    public static bool IsCloudPath(string? path)
    {
        try
        {
            var kind = Parse(path).Kind;
            return kind != PathSchemeKind.Local && kind != PathSchemeKind.File;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when the path has any recognized scheme, including file://.
    /// A malformed bracketed cloud path has no usable scheme.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>True if a recognized scheme is present.</returns>
    public static bool HasScheme(string? path)
    {
        try
        {
            return Parse(path).Kind != PathSchemeKind.Local;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// The canonical scheme prefix for a kind, or null for Local.
    /// </summary>
    /// <param name="kind">The scheme kind.</param>
    /// <returns>"file", "s3", "gs", "gdrive", "azure", "dropbox", "onedrive", or null.</returns>
    public static string? GetSchemePrefix(PathSchemeKind kind) => kind switch
    {
        PathSchemeKind.Local       => null,
        PathSchemeKind.File        => "file",
        PathSchemeKind.S3          => "s3",
        PathSchemeKind.Gcs         => "gs",
        PathSchemeKind.GoogleDrive => "gdrive",
        PathSchemeKind.AzureBlob   => "azure",
        PathSchemeKind.Dropbox     => "dropbox",
        PathSchemeKind.OneDrive    => "onedrive",
        _                          => null,
    };

    /// <summary>
    /// The kind for a scheme prefix. Case-insensitive.
    /// </summary>
    /// <param name="schemePrefix">
    /// The scheme without "://" (for example, "s3" or "S3").
    /// </param>
    /// <returns>
    /// The matching kind, or <see cref="PathSchemeKind.Local"/> if the
    /// prefix is not recognized. Never throws.
    /// </returns>
    public static PathSchemeKind GetKind(string? schemePrefix)
    {
        if (string.IsNullOrWhiteSpace(schemePrefix))
        {
            return PathSchemeKind.Local;
        }

        var s = schemePrefix.Trim().ToLowerInvariant();

        return s switch
        {
            "file"    => PathSchemeKind.File,
            "s3"      => PathSchemeKind.S3,
            "gs"      => PathSchemeKind.Gcs,
            "gdrive"  => PathSchemeKind.GoogleDrive,
            "azure"   => PathSchemeKind.AzureBlob,
            "dropbox" => PathSchemeKind.Dropbox,
            "onedrive" => PathSchemeKind.OneDrive,
            _         => PathSchemeKind.Local,
        };
    }
}
