// ============================================================================
// FILE:     src/CodeViewer.Platform/State/ProjectPathNormalizer.cs
// PURPOSE:  Normalizes a project root path before it is used as a
//           persistence key, so the same folder always maps to the same
//           state file regardless of how the user typed or browsed to it.
//           Handles Windows path casing, directory separators, and
//           trailing separators.
// RELATION: Used by JsonWorkspaceStateStore to compute the SHA-256 state
//           file name and to embed the canonical path inside the file.
// ============================================================================

using System.Security.Cryptography;
using System.Text;

namespace CodeViewer.Platform.State;

/// <summary>
/// Normalizes project paths and derives persistence keys from them.
/// </summary>
public static class ProjectPathNormalizer
{
    /// <summary>
    /// Returns the canonical form of a project path: absolute, unified to
    /// the platform directory separator, with any trailing separator
    /// removed. On Windows the result is also upper-cased for key
    /// comparison because the file system is case-insensitive; the
    /// display path is preserved separately.
    /// </summary>
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Resolve to an absolute path (handles relative input and . / ..).
        var full = Path.GetFullPath(path.Trim());

        // Unify separators to the platform separator.
        full = full.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        // Remove trailing separators, but never reduce a drive root
        // ("C:\") or UNC root to an empty/invalid string.
        full = TrimTrailingSeparators(full);

        // On case-insensitive file systems (Windows, macOS by default)
        // upper-case so "c:\Proj" and "C:\proj" map to the same key.
        if (!IsCaseSensitiveFileSystem())
        {
            full = full.ToUpperInvariant();
        }

        return full;
    }

    /// <summary>
    /// The canonical path exactly as it should be shown to the user:
    /// absolute and separator-unified, but with original casing preserved.
    /// </summary>
    public static string NormalizeForDisplay(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path.Trim());
        full = full.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return TrimTrailingSeparators(full);
    }

    /// <summary>
    /// The persistence key for a project path: lowercase hex SHA-256 of
    /// the normalized path. Stable across sessions for the same folder.
    /// </summary>
    public static string ComputeKey(string path)
    {
        var normalized = Normalize(path);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ------------------------------------------------------------------

    private static string TrimTrailingSeparators(string path)
    {
        // Never trim a root like "C:\" or "\\server\share\".
        var root = Path.GetPathRoot(path);
        var trimmed = path.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return trimmed.Length == 0 || trimmed.Equals(root?.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase)
            ? path
            : trimmed;
    }

    private static bool IsCaseSensitiveFileSystem()
    {
        // Windows and macOS (default) are case-insensitive; Linux is not.
        return OperatingSystem.IsLinux();
    }
}
