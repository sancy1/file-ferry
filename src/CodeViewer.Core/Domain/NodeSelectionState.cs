// ============================================================================
// FILE:     src/CodeViewer.Core/Domain/NodeSelectionState.cs
// PURPOSE:  The tri-state checkbox value of a project node. A directory is
//           Partial when some but not all of its loaded descendants are
//           checked. Selection is independent of expansion.
// ============================================================================

namespace CodeViewer.Core.Domain;

/// <summary>
/// The selection state of a <see cref="ProjectNode"/>.
/// </summary>
public enum NodeSelectionState
{
    /// <summary>Neither the node nor any of its descendants is checked.</summary>
    Unchecked,

    /// <summary>The node and every one of its descendants is checked.</summary>
    Checked,

    /// <summary>
    /// Directory only: the node itself is not fully checked, but at least
    /// one descendant is checked. Rendered as an indeterminate checkbox.
    /// </summary>
    Partial,
}
