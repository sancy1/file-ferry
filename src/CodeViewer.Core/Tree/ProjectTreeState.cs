// ============================================================================
// FILE:     src/CodeViewer.Core/Tree/ProjectTreeState.cs
// PURPOSE:  The single per-project selection engine. Owns the node tree,
//           expansion state, tri-state selection, the selected file, the
//           Auto Display flag, and the display mode. All three display
//           modes (Structure Only, Code Only, Structure + Code) consume
//           this one state, so switching modes never loses selections.
// RELATION: Pure domain logic with no I/O. The platform enumerator
//           supplies children; the App view models bind to the projections
//           this class exposes. One instance exists per open project tab.
// ============================================================================

using CodeViewer.Core.Domain;

namespace CodeViewer.Core.Tree;

/// <summary>
/// The complete workspace state of one open project.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lazy loading.</b> A project opens with only its root node, collapsed
/// and unchecked. Children are attached by <see cref="SetChildren"/> when
/// the user first expands a directory. No recursive scan ever happens at
/// open time.
/// </para>
/// <para>
/// <b>Selection rules.</b> Checking a directory checks every loaded
/// descendant recursively; unchecking removes all descendants. A directory
/// with some but not all descendants checked is
/// <see cref="NodeSelectionState.Partial"/>. Selection and expansion are
/// independent: collapsing a folder never changes what is selected.
/// </para>
/// <para>
/// <b>Auto Display.</b> ON checks every loaded node and expands every
/// directory; the platform layer drives the full walk and calls
/// <see cref="SetChildren"/> as it goes. OFF unchecks everything and
/// clears the viewers but preserves expansion exactly as it was.
/// </para>
/// </remarks>
public sealed class ProjectTreeState
{
    /// <summary>
    /// Creates the state for a project. The root starts collapsed,
    /// unchecked, and not loaded.
    /// </summary>
    public ProjectTreeState(string projectName, string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        ProjectName = projectName;
        Root = new ProjectNode(NodeKind.Directory, projectName, rootPath, parent: null);
    }

    /// <summary>The display name of the project (the root folder name).</summary>
    public string ProjectName { get; }

    /// <summary>The root directory node. Never null.</summary>
    public ProjectNode Root { get; }

    /// <summary>The active presentation. Defaults to Structure + Code.</summary>
    public DisplayMode DisplayMode { get; set; } = DisplayMode.StructureAndCode;

    /// <summary>
    /// Whether Auto Display is on. Always false for a newly opened
    /// project; set through <see cref="SetAutoDisplayOn"/> and
    /// <see cref="SetAutoDisplayOff"/> so the selection rules are applied.
    /// </summary>
    public bool AutoDisplay { get; private set; }

    /// <summary>
    /// The file whose code card is highlighted, or null. Setting this
    /// never changes checkbox selection.
    /// </summary>
    public ProjectNode? SelectedFile { get; set; }

    // ------------------------------------------------------------------
    // Lazy loading
    // ------------------------------------------------------------------

    /// <summary>
    /// Attaches enumerated children to a directory and marks it loaded.
    /// Children are sorted directories-first, then files, each ordinal
    /// alphabetically, matching the reference design. Called by the
    /// platform layer after enumeration and by tests.
    /// </summary>
    public void SetChildren(ProjectNode directory, IEnumerable<ProjectNode> children)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(children);
        if (directory.Kind != NodeKind.Directory)
        {
            throw new InvalidOperationException("Only directories can have children.");
        }

        var ordered = children
            .OrderBy(c => c.Kind == NodeKind.Directory ? 0 : 1)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        directory.SetChildren(ordered);
        directory.LoadState = NodeLoadState.Loaded;

        // If the directory is checked, newly arrived children inherit the
        // check (checking a parent covers its descendants). If it is
        // unchecked, children stay unchecked. Either way the rollup is
        // recomputed so a Partial parent stays consistent.
        if (directory.Selection == NodeSelectionState.Checked)
        {
            foreach (var child in ordered)
            {
                SetCheckedRecursive(child, true);
            }
        }

        RecomputeRollup();
    }

    /// <summary>Marks a directory as currently enumerating.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1822:Mark members as static",
        Justification = "Instance API by design; Phase 5 raises change notifications from these members.")]
    public void MarkLoading(ProjectNode directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        directory.LoadState = NodeLoadState.Loading;
    }

    /// <summary>
    /// Marks a directory's enumeration as failed. The directory stays
    /// visible; the user can retry by collapsing and re-expanding.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1822:Mark members as static",
        Justification = "Instance API by design; Phase 5 raises change notifications from these members.")]
    public void MarkFailed(ProjectNode directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        directory.LoadState = NodeLoadState.Failed;
    }

    // ------------------------------------------------------------------
    // Expansion (independent of selection)
    // ------------------------------------------------------------------

    /// <summary>Expands a directory. Never changes selection.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1822:Mark members as static",
        Justification = "Instance API by design; Phase 5 raises change notifications from these members.")]
    public void Expand(ProjectNode directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (directory.Kind == NodeKind.Directory)
        {
            directory.IsExpanded = true;
        }
    }

    /// <summary>Collapses a directory. Never changes selection.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1822:Mark members as static",
        Justification = "Instance API by design; Phase 5 raises change notifications from these members.")]
    public void Collapse(ProjectNode directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        directory.IsExpanded = false;
    }

    // ------------------------------------------------------------------
    // Selection
    // ------------------------------------------------------------------

    /// <summary>
    /// Checks or unchecks a node. Checking a directory checks every loaded
    /// descendant recursively; unchecking removes all descendants. Ancestor
    /// rollups are recomputed so parents become Checked, Partial, or
    /// Unchecked to match.
    /// </summary>
    public void SetChecked(ProjectNode node, bool isChecked)
    {
        ArgumentNullException.ThrowIfNull(node);

        SetCheckedRecursive(node, isChecked);
        RecomputeRollup();
    }

    /// <summary>
    /// Toggles a node: Checked or Partial becomes Unchecked; Unchecked
    /// becomes Checked.
    /// </summary>
    public void ToggleChecked(ProjectNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        SetChecked(node, node.Selection != NodeSelectionState.Checked);
    }

    // ------------------------------------------------------------------
    // Auto Display
    // ------------------------------------------------------------------

    /// <summary>
    /// Turns Auto Display on: checks every loaded node and expands every
    /// loaded directory. The platform layer drives the full asynchronous
    /// walk and calls <see cref="SetChildren"/> as directories are
    /// enumerated; because a checked directory's children inherit the
    /// check, the whole tree ends up selected.
    /// </summary>
    public void SetAutoDisplayOn()
    {
        AutoDisplay = true;
        SetCheckedRecursive(Root, true);
        ExpandAllRecursive(Root);
        RecomputeRollup();
    }

    /// <summary>
    /// Turns Auto Display off: unchecks every node and clears the selected
    /// file, but preserves expansion exactly as it was. The user can then
    /// browse and select manually.
    /// </summary>
    public void SetAutoDisplayOff()
    {
        AutoDisplay = false;
        SetCheckedRecursive(Root, false);
        SelectedFile = null;
        RecomputeRollup();
    }

    // ------------------------------------------------------------------
    // Projections consumed by the views
    // ------------------------------------------------------------------

    /// <summary>
    /// The flattened list of rows currently visible in the Project
    /// Browser: a node appears when every ancestor is expanded. This is
    /// the projection the virtualized browser binds to.
    /// </summary>
    public IReadOnlyList<ProjectNode> GetVisibleRows()
    {
        var rows = new List<ProjectNode>();
        AppendVisible(Root, rows);
        return rows;
    }

    /// <summary>
    /// Every checked file, in tree order. This is the set the Code
    /// Content viewer renders as cards.
    /// </summary>
    public IReadOnlyList<ProjectNode> GetSelectedFiles()
    {
        var files = new List<ProjectNode>();
        CollectCheckedFiles(Root, files);
        return files;
    }

    /// <summary>
    /// Every checked node (files and directories), in tree order. This is
    /// the set the Project Tree Structure viewer renders.
    /// </summary>
    public IReadOnlyList<ProjectNode> GetSelectedNodes()
    {
        var nodes = new List<ProjectNode>();
        CollectCheckedNodes(Root, nodes);
        return nodes;
    }

    /// <summary>
    /// Every node currently materialized in the tree, in tree order.
    /// Used when capturing workspace state for persistence.
    /// </summary>
    public IReadOnlyList<ProjectNode> GetAllNodes()
    {
        var nodes = new List<ProjectNode>();
        CollectAll(Root, nodes);
        return nodes;
    }

    /// <summary>
    /// Restores the Auto Display flag without checking or expanding
    /// anything. Used when reopening a project that was saved with Auto
    /// Display on: the saved selections are re-applied by path, and this
    /// only reinstates the flag so the toggle reflects the saved state.
    /// It never triggers a fresh scan.
    /// </summary>
    public void RestoreAutoDisplayFlag()
    {
        AutoDisplay = true;
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    private static void SetCheckedRecursive(ProjectNode node, bool isChecked)
    {
        node.Selection = isChecked
            ? NodeSelectionState.Checked
            : NodeSelectionState.Unchecked;

        foreach (var child in node.Children)
        {
            SetCheckedRecursive(child, isChecked);
        }
    }

    private static void ExpandAllRecursive(ProjectNode node)
    {
        if (node.Kind == NodeKind.Directory)
        {
            node.IsExpanded = true;
        }

        foreach (var child in node.Children)
        {
            ExpandAllRecursive(child);
        }
    }

    /// <summary>
    /// Recomputes directory selection bottom-up from the leaves so a
    /// directory is Checked when all children are checked, Unchecked when
    /// none are, and Partial otherwise. A directory with no loaded
    /// children keeps its own explicit state.
    /// </summary>
    private void RecomputeRollup()
    {
        RecomputeNode(Root);
    }

    private static NodeSelectionState RecomputeNode(ProjectNode node)
    {
        if (node.Kind == NodeKind.File || node.Children.Count == 0)
        {
            return node.Selection;
        }

        var anyChecked = false;
        var anyUnchecked = false;

        foreach (var child in node.Children)
        {
            var childState = RecomputeNode(child);
            if (childState == NodeSelectionState.Checked)
            {
                anyChecked = true;
            }
            else if (childState == NodeSelectionState.Unchecked)
            {
                anyUnchecked = true;
            }
            else
            {
                // Partial child: counts as both for the parent.
                anyChecked = true;
                anyUnchecked = true;
            }
        }

        node.Selection = (anyChecked, anyUnchecked) switch
        {
            (true, false)  => NodeSelectionState.Checked,
            (false, true)  => NodeSelectionState.Unchecked,
            (true, true)   => NodeSelectionState.Partial,
            (false, false) => node.Selection,
        };

        return node.Selection;
    }

    private static void AppendVisible(ProjectNode node, List<ProjectNode> rows)
    {
        rows.Add(node);
        if (!node.IsExpanded)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            AppendVisible(child, rows);
        }
    }

    private static void CollectCheckedFiles(ProjectNode node, List<ProjectNode> files)
    {
        if (node.Kind == NodeKind.File && node.Selection == NodeSelectionState.Checked)
        {
            files.Add(node);
        }

        foreach (var child in node.Children)
        {
            CollectCheckedFiles(child, files);
        }
    }

    private static void CollectCheckedNodes(ProjectNode node, List<ProjectNode> nodes)
    {
        if (node.Selection == NodeSelectionState.Checked)
        {
            nodes.Add(node);
            // A fully checked directory contributes itself; its children
            // are still collected so the structure shows the full subtree.
        }

        foreach (var child in node.Children)
        {
            CollectCheckedNodes(child, nodes);
        }
    }

    private static void CollectAll(ProjectNode node, List<ProjectNode> nodes)
    {
        nodes.Add(node);
        foreach (var child in node.Children)
        {
            CollectAll(child, nodes);
        }
    }
}
