// ============================================================================
// FILE:     src/FileHarvester.Tests/CodeViewer/StructureTreeRendererTests.cs
// PURPOSE:  Unit tests for the ASCII structure tree renderer: glyph usage,
//           line counting, and omission of unchecked subtrees.
// ============================================================================

using CodeViewer.Core.Domain;
using CodeViewer.Core.Tree;
using FluentAssertions;

namespace FileHarvester.Tests.CodeViewer;

public sealed class StructureTreeRendererTests
{
    private static readonly string[] ExpectedFullTree =
    {
        "Demo/",
        "├── src/",
        "│   ├── __pycache__/",
        "│   └── main.py",
        "└── README.md",
    };

    private static (ProjectTreeState State, ProjectNode Root, ProjectNode Src) BuildTree()
    {
        var state = new ProjectTreeState("Demo", @"C:\projects\Demo");
        var root = state.Root;
        var src = new ProjectNode(NodeKind.Directory, "src", @"C:\projects\Demo\src", root);
        var readme = new ProjectNode(NodeKind.File, "README.md", @"C:\projects\Demo\README.md", root);
        state.SetChildren(root, new[] { src, readme });
        var main = new ProjectNode(NodeKind.File, "main.py", @"C:\projects\Demo\src\main.py", src);
        var cache = new ProjectNode(NodeKind.Directory, "__pycache__", @"C:\projects\Demo\src\__pycache__", src);
        state.SetChildren(src, new[] { cache, main });
        return (state, root, src);
    }

    [Fact]
    public void RenderNothingCheckedReturnsEmpty()
    {
        var (_, root, _) = BuildTree();

        StructureTreeRenderer.Render(root).Should().BeEmpty();
        StructureTreeRenderer.CountLines(root).Should().Be(0);
    }

    [Fact]
    public void RenderFullyCheckedProducesBoxDrawingTree()
    {
        var (state, root, _) = BuildTree();
        state.SetAutoDisplayOn();

        var text = StructureTreeRenderer.Render(root);

        var expected = string.Join(Environment.NewLine, ExpectedFullTree);
        text.Should().Be(expected);
    }

    [Fact]
    public void RenderPartialSelectionOmitsUncheckedSubtrees()
    {
        var (state, root, src) = BuildTree();
        // Check only main.py inside src.
        var main = src.Children.Single(c => c.Name == "main.py");
        state.SetChecked(main, true);

        var text = StructureTreeRenderer.Render(root);

        text.Should().Contain("src/");
        text.Should().Contain("main.py");
        text.Should().NotContain("README.md");
        text.Should().NotContain("__pycache__");
    }

    [Fact]
    public void CountLinesMatchesRenderedLineCount()
    {
        var (state, root, _) = BuildTree();
        state.SetAutoDisplayOn();

        var text = StructureTreeRenderer.Render(root);
        var renderedLines = text.Split(Environment.NewLine).Length;

        StructureTreeRenderer.CountLines(root).Should().Be(renderedLines);
    }

    [Fact]
    public void RenderDirectoriesHaveTrailingSlashFilesDoNot()
    {
        var (state, root, _) = BuildTree();
        state.SetAutoDisplayOn();

        var text = StructureTreeRenderer.Render(root);

        text.Should().Contain("src/");
        text.Should().Contain("__pycache__/");
        text.Should().Contain("main.py");
        text.Should().NotContain("main.py/");
    }
}
