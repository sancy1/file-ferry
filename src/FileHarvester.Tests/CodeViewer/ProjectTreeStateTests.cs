// ============================================================================
// FILE:     src/FileHarvester.Tests/CodeViewer/ProjectTreeStateTests.cs
// PURPOSE:  Unit tests for the CodeViewer selection engine: lazy loading,
//           tri-state recursive selection, expansion independence, Auto
//           Display on/off semantics, and the view projections.
// ============================================================================

using CodeViewer.Core.Domain;
using CodeViewer.Core.Tree;
using FluentAssertions;

namespace FileHarvester.Tests.CodeViewer;

public sealed class ProjectTreeStateTests
{
    // Builds: root/ (dir) -> src/ (dir) -> main.py, util.py ; README.md
    private static (ProjectTreeState State, ProjectNode Root, ProjectNode Src,
        ProjectNode Main, ProjectNode Util, ProjectNode Readme) BuildTree()
    {
        var state = new ProjectTreeState("Demo", @"C:\projects\Demo");
        var root = state.Root;

        var src = new ProjectNode(NodeKind.Directory, "src", @"C:\projects\Demo\src", root);
        var readme = new ProjectNode(NodeKind.File, "README.md", @"C:\projects\Demo\README.md", root);
        state.SetChildren(root, new[] { readme, src }); // unsorted on purpose

        var main = new ProjectNode(NodeKind.File, "main.py", @"C:\projects\Demo\src\main.py", src);
        var util = new ProjectNode(NodeKind.File, "util.py", @"C:\projects\Demo\src\util.py", src);
        state.SetChildren(src, new[] { util, main });

        return (state, root, src, main, util, readme);
    }

    [Fact]
    public void NewProjectRootIsCollapsedUncheckedAndNotLoaded()
    {
        var state = new ProjectTreeState("Demo", @"C:\projects\Demo");

        state.Root.IsExpanded.Should().BeFalse();
        state.Root.Selection.Should().Be(NodeSelectionState.Unchecked);
        state.Root.LoadState.Should().Be(NodeLoadState.NotLoaded);
        state.AutoDisplay.Should().BeFalse();
        state.DisplayMode.Should().Be(DisplayMode.StructureAndCode);
    }

    [Fact]
    public void SetChildrenSortsDirectoriesFirstThenFilesAlphabetically()
    {
        var (state, root, _, _, _, _) = BuildTree();

        root.Children.Select(c => c.Name).Should().Equal("src", "README.md");
        root.Children[0].Kind.Should().Be(NodeKind.Directory);
        root.LoadState.Should().Be(NodeLoadState.Loaded);
    }

    [Fact]
    public void CheckingParentChecksAllDescendantsRecursively()
    {
        var (state, root, src, main, util, _) = BuildTree();

        state.SetChecked(src, true);

        src.Selection.Should().Be(NodeSelectionState.Checked);
        main.Selection.Should().Be(NodeSelectionState.Checked);
        util.Selection.Should().Be(NodeSelectionState.Checked);
        // Root has one checked child (src) and one unchecked (README) -> Partial.
        root.Selection.Should().Be(NodeSelectionState.Partial);
    }

    [Fact]
    public void UncheckingParentRemovesAllDescendants()
    {
        var (state, _, src, main, util, _) = BuildTree();
        state.SetChecked(src, true);

        state.SetChecked(src, false);

        src.Selection.Should().Be(NodeSelectionState.Unchecked);
        main.Selection.Should().Be(NodeSelectionState.Unchecked);
        util.Selection.Should().Be(NodeSelectionState.Unchecked);
    }

    [Fact]
    public void CheckingSingleFileMakesAncestorsPartial()
    {
        var (state, root, src, main, util, _) = BuildTree();

        state.SetChecked(main, true);

        main.Selection.Should().Be(NodeSelectionState.Checked);
        util.Selection.Should().Be(NodeSelectionState.Unchecked);
        src.Selection.Should().Be(NodeSelectionState.Partial);
        root.Selection.Should().Be(NodeSelectionState.Partial);
    }

    [Fact]
    public void CheckingAllChildrenMakesParentChecked()
    {
        var (state, _, src, main, util, _) = BuildTree();

        state.SetChecked(main, true);
        state.SetChecked(util, true);

        src.Selection.Should().Be(NodeSelectionState.Checked);
    }

    [Fact]
    public void ChildrenOfCheckedDirectoryInheritCheckWhenLoaded()
    {
        var (state, root, _, _, _, _) = BuildTree();
        state.SetChecked(root, true);

        // A directory enumerated after its parent was checked inherits the check.
        var src = root.Children[0];
        var late = new ProjectNode(NodeKind.File, "late.py", @"C:\projects\Demo\src\late.py", src);
        state.SetChildren(src, new[] { late });

        late.Selection.Should().Be(NodeSelectionState.Checked);
    }

    [Fact]
    public void ExpansionIsIndependentOfSelection()
    {
        var (state, _, src, main, _, _) = BuildTree();
        state.SetChecked(main, true);

        state.Expand(src);
        src.IsExpanded.Should().BeTrue();
        main.Selection.Should().Be(NodeSelectionState.Checked);

        state.Collapse(src);
        src.IsExpanded.Should().BeFalse();
        main.Selection.Should().Be(NodeSelectionState.Checked);
    }

    [Fact]
    public void AutoDisplayOnChecksAndExpandsEverything()
    {
        var (state, root, src, main, util, readme) = BuildTree();

        state.SetAutoDisplayOn();

        state.AutoDisplay.Should().BeTrue();
        root.Selection.Should().Be(NodeSelectionState.Checked);
        src.Selection.Should().Be(NodeSelectionState.Checked);
        main.Selection.Should().Be(NodeSelectionState.Checked);
        util.Selection.Should().Be(NodeSelectionState.Checked);
        readme.Selection.Should().Be(NodeSelectionState.Checked);
        root.IsExpanded.Should().BeTrue();
        src.IsExpanded.Should().BeTrue();
    }

    [Fact]
    public void AutoDisplayOffUnchecksAllButPreservesExpansion()
    {
        var (state, root, src, main, _, _) = BuildTree();
        state.SetAutoDisplayOn();
        state.SelectedFile = main;

        state.SetAutoDisplayOff();

        state.AutoDisplay.Should().BeFalse();
        root.Selection.Should().Be(NodeSelectionState.Unchecked);
        src.Selection.Should().Be(NodeSelectionState.Unchecked);
        main.Selection.Should().Be(NodeSelectionState.Unchecked);
        state.SelectedFile.Should().BeNull();
        // Expansion preserved.
        root.IsExpanded.Should().BeTrue();
        src.IsExpanded.Should().BeTrue();
    }

    [Fact]
    public void GetVisibleRowsOnlyIncludesExpandedDescendants()
    {
        var (state, root, src, main, util, readme) = BuildTree();

        // Nothing expanded: only the root row.
        state.GetVisibleRows().Should().Equal(root);

        state.Expand(root);
        state.GetVisibleRows().Should().Equal(root, src, readme);

        state.Expand(src);
        // src's children are sorted alphabetically: main.py before util.py.
        state.GetVisibleRows().Should().Equal(root, src, main, util, readme);
    }

    [Fact]
    public void GetSelectedFilesReturnsCheckedFilesInTreeOrder()
    {
        var (state, root, _, main, util, readme) = BuildTree();
        state.SetChecked(main, true);
        state.SetChecked(readme, true);

        state.GetSelectedFiles().Should().Equal(main, readme);
    }

    [Fact]
    public void GetSelectedNodesIncludesCheckedDirectoriesAndFiles()
    {
        var (state, root, src, main, util, _) = BuildTree();
        state.SetChecked(src, true);

        var selected = state.GetSelectedNodes();
        selected.Should().Contain(src);
        selected.Should().Contain(main);
        selected.Should().Contain(util);
        selected.Should().NotContain(root); // root is only Partial
    }

    [Fact]
    public void MarkLoadingAndFailedUpdateLoadState()
    {
        var (state, _, src, _, _, _) = BuildTree();

        state.MarkLoading(src);
        src.LoadState.Should().Be(NodeLoadState.Loading);

        state.MarkFailed(src);
        src.LoadState.Should().Be(NodeLoadState.Failed);
    }

    [Fact]
    public void SetChildrenOnFileThrows()
    {
        var (state, _, _, main, _, _) = BuildTree();

        var act = () => state.SetChildren(main, Array.Empty<ProjectNode>());

        act.Should().Throw<InvalidOperationException>();
    }
}
