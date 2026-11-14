// ============================================================================
// FILE:     src/FileHarvester.Tests/CodeViewer/ProjectTreeEnumeratorTests.cs
// PURPOSE:  Tests for the lazy project tree enumerator: one-level
//           enumeration, exclusion rules, cancellation, and tolerance of
//           per-entry filesystem errors. Uses real temp directories.
// ============================================================================

using CodeViewer.Core.Domain;
using CodeViewer.Platform.FileSystem;
using FileHarvester.Core.Composition;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.Tests.CodeViewer;

public sealed class ProjectTreeEnumeratorTests : IDisposable
{
    private readonly string _root;
    private readonly ProjectTreeEnumerator _enumerator;

    public ProjectTreeEnumeratorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cv-enum-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var services = new ServiceCollection();
        services.AddFileHarvester(new ResilienceConfig(
            LogDirectory: Path.Combine(_root, "logs"),
            EnableConsoleSink: false,
            EnableFileSink: false));
        var provider = services.BuildServiceProvider();
        _enumerator = new ProjectTreeEnumerator(provider.GetRequiredService<IResilienceExecutor>());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private ProjectNode RootNode() =>
        new(NodeKind.Directory, "root", _root, parent: null);

    private async Task<List<ProjectNode>> CollectAsync(
        ProjectNode dir, bool showSystemFiles = false, CancellationToken ct = default)
    {
        var list = new List<ProjectNode>();
        await foreach (var node in _enumerator.EnumerateChildrenAsync(dir, showSystemFiles, cancellationToken: ct))
        {
            list.Add(node);
        }

        return list;
    }

    [Fact]
    public async Task EnumeratesOnlyOneLevel()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        Directory.CreateDirectory(Path.Combine(_root, "src", "nested"));
        File.WriteAllText(Path.Combine(_root, "src", "main.py"), "print(1)");
        File.WriteAllText(Path.Combine(_root, "README.md"), "hi");

        var children = await CollectAsync(RootNode());

        // Only the immediate level: src/ and README.md. nested/ and
        // main.py are NOT returned because we do not recurse.
        children.Select(c => c.Name).Should().BeEquivalentTo("src", "README.md");
        children.Single(c => c.Name == "src").Kind.Should().Be(NodeKind.Directory);
        children.Single(c => c.Name == "README.md").Kind.Should().Be(NodeKind.File);
    }

    [Fact]
    public async Task OsClutterIsHiddenByDefaultAndRevealedByToggle()
    {
        File.WriteAllText(Path.Combine(_root, "Thumbs.db"), "x");
        File.WriteAllText(Path.Combine(_root, "main.py"), "print(1)");
        Directory.CreateDirectory(Path.Combine(_root, "__pycache__"));

        var hidden = await CollectAsync(RootNode(), showSystemFiles: false);
        hidden.Select(c => c.Name).Should().BeEquivalentTo("__pycache__", "main.py");

        var shown = await CollectAsync(RootNode(), showSystemFiles: true);
        shown.Select(c => c.Name).Should().Contain("Thumbs.db");
    }

    [Fact]
    public async Task MissingDirectoryYieldsNoChildrenAndDoesNotThrow()
    {
        var missing = new ProjectNode(
            NodeKind.Directory, "gone", Path.Combine(_root, "gone"), parent: null);

        var children = await CollectAsync(missing);

        children.Should().BeEmpty();
    }

    [Fact]
    public async Task CancellationStopsEnumeration()
    {
        for (var i = 0; i < 200; i++)
        {
            File.WriteAllText(Path.Combine(_root, $"f{i}.txt"), "x");
        }

        using var cts = new CancellationTokenSource();
        var collected = new List<ProjectNode>();

        var act = async () =>
        {
            await foreach (var node in _enumerator.EnumerateChildrenAsync(
                               RootNode(), showSystemFiles: false, cancellationToken: cts.Token))
            {
                collected.Add(node);
                // Cancel after enumeration has started so the token is
                // observed between entries, not before the first call.
                cts.Cancel();
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        collected.Count.Should().BeLessThan(200);
    }

    [Fact]
    public async Task ReparsePointsAreNotFollowed()
    {
        // Create a real directory and a junction pointing back at the root
        // so a naive walk would loop. IgnoreInaccessible + AttributesToSkip
        // must keep the junction out.
        var real = Directory.CreateDirectory(Path.Combine(_root, "real"));
        File.WriteAllText(Path.Combine(real.FullName, "a.txt"), "x");
        var link = Path.Combine(_root, "loop");
        try
        {
            Directory.CreateSymbolicLink(link, _root);
        }
        catch (Exception)
        {
            // Symlink creation may require privilege; skip the assertion.
            return;
        }

        var children = await CollectAsync(RootNode());

        children.Select(c => c.Name).Should().NotContain("loop");
    }
}
