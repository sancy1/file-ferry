// ============================================================================
// FILE:     src/CodeViewer.Core/Domain/NodeLoadState.cs
// PURPOSE:  Tracks lazy directory loading. A directory's children are
//           enumerated only when the user first expands it, so opening a
//           project never performs a full recursive scan.
// ============================================================================

namespace CodeViewer.Core.Domain;

/// <summary>
/// The lazy-load state of a directory <see cref="ProjectNode"/>.
/// </summary>
public enum NodeLoadState
{
    /// <summary>Children have not been enumerated yet.</summary>
    NotLoaded,

    /// <summary>Children are being enumerated on a background thread.</summary>
    Loading,

    /// <summary>Children have been enumerated and are attached.</summary>
    Loaded,

    /// <summary>
    /// Enumeration failed (for example permission denied). The directory
    /// stays visible in the browser; the user may retry by re-expanding.
    /// </summary>
    Failed,
}
