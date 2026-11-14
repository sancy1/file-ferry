// ============================================================================
// FILE:     src/CodeViewer.Core/Domain/ProjectNode.cs
// PURPOSE:  One node in a project's folder/file tree. Nodes are created
//           lazily: a project opens with only its root node, and a
//           directory's children are attached the first time the directory
//           is expanded. Selection is tri-state and independent of
//           expansion.
// RELATION: Owned and mutated exclusively by ProjectTreeState, which is
//           the single selection engine all three display modes consume.
// ============================================================================

namespace CodeViewer.Core.Domain;

/// <summary>
/// A directory or file in the project tree.
/// </summary>
public sealed class ProjectNode
{
    private readonly List<ProjectNode> _children = new();

    /// <summary>
    /// Creates a node. Only <see cref="ProjectTreeState"/> and tests build
    /// nodes directly; production nodes come from the platform enumerator.
    /// </summary>
    public ProjectNode(NodeKind kind, string name, string fullPath, ProjectNode? parent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        Kind = kind;
        Name = name;
        FullPath = fullPath;
        Parent = parent;
    }

    /// <summary>Whether this node is a directory or a file.</summary>
    public NodeKind Kind { get; }

    /// <summary>The display name (file or folder name, not the full path).</summary>
    public string Name { get; }

    /// <summary>The absolute path on disk.</summary>
    public string FullPath { get; }

    /// <summary>The parent directory, or null for the project root.</summary>
    public ProjectNode? Parent { get; }

    /// <summary>
    /// The children attached so far. Empty until a directory is expanded
    /// and enumerated. Ordered directories-first, then files, each
    /// alphabetically, matching the reference design.
    /// </summary>
    public IReadOnlyList<ProjectNode> Children => _children;

    /// <summary>Whether the node is expanded in the Project Browser.</summary>
    public bool IsExpanded { get; internal set; }

    /// <summary>The tri-state selection value shown by the checkbox.</summary>
    public NodeSelectionState Selection { get; internal set; } = NodeSelectionState.Unchecked;

    /// <summary>The lazy-load state. Always <see cref="NodeLoadState.Loaded"/> for files.</summary>
    public NodeLoadState LoadState { get; internal set; } = NodeLoadState.NotLoaded;

    /// <summary>
    /// True when this file cannot be previewed because it is binary or an
    /// unsupported format. The node remains visible and selectable in the
    /// Project Browser and appears in the Project Tree Structure; only its
    /// code card shows a no-preview notice. Always false for directories.
    /// </summary>
    public bool IsPreviewUnavailable { get; internal set; }

    /// <summary>Attaches children. Called by the tree state after enumeration.</summary>
    internal void SetChildren(IEnumerable<ProjectNode> children)
    {
        _children.Clear();
        _children.AddRange(children);
    }

    /// <summary>Removes all children (used when a directory is refreshed).</summary>
    internal void ClearChildren() => _children.Clear();

    /// <summary>Depth in the tree; the root is 0. Used for row indentation.</summary>
    public int Depth
    {
        get
        {
            var depth = 0;
            for (var node = Parent; node is not null; node = node.Parent)
            {
                depth++;
            }

            return depth;
        }
    }

    public override string ToString() => $"{Kind} {FullPath} [{Selection}]";
}
