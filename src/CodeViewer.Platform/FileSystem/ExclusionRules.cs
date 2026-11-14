// ============================================================================
// FILE:     src/CodeViewer.Platform/FileSystem/ExclusionRules.cs
// PURPOSE:  The single, explicit list of entries CodeViewer hides by
//           default. Only true operating-system clutter is hidden, and only
//           from automatic display; a "Show system files" toggle reveals
//           them. Generated and hidden project directories such as
//           __pycache__, .git, node_modules, bin, and obj are NEVER
//           excluded - they are part of the project and appear in the
//           reference design.
// RELATION: Consulted by ProjectTreeEnumerator for every entry. The
//           per-project ShowSystemFiles flag (persisted in workspace state)
//           is passed in by the caller.
// ============================================================================

namespace CodeViewer.Platform.FileSystem;

/// <summary>
/// Decides which directory entries are hidden from automatic display.
/// </summary>
/// <remarks>
/// <para>
/// The list is deliberately minimal and contains only files the operating
/// system creates for its own bookkeeping, never project content:
/// <c>.DS_Store</c> and <c>Thumbs.db</c> / <c>desktop.ini</c> (macOS and
/// Windows shell metadata), and <c>$RECYCLE.BIN</c> /
/// <c>System Volume Information</c> (Windows drive system folders).
/// </para>
/// <para>
/// Nothing else is excluded. In particular <c>__pycache__</c>, <c>.git</c>,
/// <c>node_modules</c>, <c>bin</c>, <c>obj</c>, and every hidden or
/// generated directory remain visible, because they are part of the
/// project the user opened and the reference design shows them.
/// </para>
/// <para>
/// Exclusion here means "hidden from automatic display", never "deleted
/// from the project". When the user turns on "Show system files" the same
/// entries appear in the Project Browser like any other.
/// </para>
/// </remarks>
public static class ExclusionRules
{
    private static readonly HashSet<string> OsClutterNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".DS_Store",
            "Thumbs.db",
            "desktop.ini",
            "$RECYCLE.BIN",
            "System Volume Information",
        };

    /// <summary>
    /// Returns true when the entry should be hidden from automatic
    /// display. When <paramref name="showSystemFiles"/> is true nothing is
    /// hidden.
    /// </summary>
    /// <param name="name">The file or directory name (not the full path).</param>
    /// <param name="showSystemFiles">
    /// The per-project "Show system files" toggle. When true, every entry
    /// is shown.
    /// </param>
    public static bool IsHidden(string name, bool showSystemFiles)
    {
        if (showSystemFiles)
        {
            return false;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return OsClutterNames.Contains(name);
    }
}
