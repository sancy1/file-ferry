// ============================================================================
// FILE:     src/CodeViewer.Core/Tree/StructureTreeRenderer.cs
// PURPOSE:  Renders the checked nodes of a project as the ASCII directory
//           tree shown in the Project Tree Structure viewer, using the
//           box-drawing glyphs from the reference design (├── └── │).
//           Also reports the line count shown in the pane header.
// RELATION: Consumes ProjectTreeState.GetSelectedNodes(). Pure text
//           generation with no I/O.
// ============================================================================

using System.Text;
using CodeViewer.Core.Domain;

namespace CodeViewer.Core.Tree;

/// <summary>
/// Produces the Project Tree Structure text.
/// </summary>
public static class StructureTreeRenderer
{
    /// <summary>
    /// Renders the checked subtree of <paramref name="root"/> as an ASCII
    /// tree. The root renders as its name with a trailing separator;
    /// directories render with a trailing separator; files render plain.
    /// Unchecked subtrees are omitted entirely.
    /// </summary>
    /// <returns>
    /// The tree text, or an empty string when nothing is checked. Lines
    /// are separated by <see cref="Environment.NewLine"/>.
    /// </returns>
    public static string Render(ProjectNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (root.Selection == NodeSelectionState.Unchecked)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.Append(root.Name).Append('/');

        var children = CheckedChildren(root).ToList();
        for (var i = 0; i < children.Count; i++)
        {
            RenderNode(children[i], prefix: string.Empty, isLast: i == children.Count - 1, builder);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The number of lines <see cref="Render"/> would produce for the
    /// tree, shown in the pane header (for example "105 lines").
    /// </summary>
    public static int CountLines(ProjectNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (root.Selection == NodeSelectionState.Unchecked)
        {
            return 0;
        }

        // One line for the root plus one per rendered descendant.
        return 1 + CountRenderedDescendants(root);
    }

    private static void RenderNode(
        ProjectNode node, string prefix, bool isLast, StringBuilder builder)
    {
        builder.AppendLine();
        builder.Append(prefix);
        builder.Append(isLast ? "└── " : "├── ");
        builder.Append(node.Name);
        if (node.Kind == NodeKind.Directory)
        {
            builder.Append('/');
        }

        var childPrefix = prefix + (isLast ? "    " : "│   ");
        var children = CheckedChildren(node).ToList();
        for (var i = 0; i < children.Count; i++)
        {
            RenderNode(children[i], childPrefix, i == children.Count - 1, builder);
        }
    }

    private static IEnumerable<ProjectNode> CheckedChildren(ProjectNode node)
        => node.Children.Where(c => c.Selection != NodeSelectionState.Unchecked);

    private static int CountRenderedDescendants(ProjectNode node)
    {
        var count = 0;
        foreach (var child in CheckedChildren(node))
        {
            count++;
            count += CountRenderedDescendants(child);
        }

        return count;
    }
}
