// ============================================================================
// FILE:     src/FileHarvester.Tests/CodeViewer/ProjectTabPersistenceTests.cs
// PURPOSE:  Tests for CodeViewer workspace persistence: D4 restore
//           (targeted re-enumeration, no full scan, newly discovered files
//           stay unchecked) and save round-trip through a tab. Uses real
//           temp directories and the in-memory state store.
// ============================================================================

using CodeViewer.Core.Domain;
using CodeViewer.Platform.FileSystem;
using CodeViewer.Platform.State;
using FileHarvester.App.ViewModels;
using FileHarvester.Core.Composition;
using FileHarvester.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.Tests.CodeViewer;

[Collection(AvaloniaCollection.Name)]
public sealed class ProjectTabPersistenceTests : IDisposable
{
    private readonly string _root;
    private readonly IResilienceExecutor _resilience;
    private readonly IWorkspaceStateStore _store;

    public ProjectTabPersistenceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cv-tab-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var services = new ServiceCollection();
        services.AddFileHarvester(new ResilienceConfig(
            LogDirectory: Path.Combine(_root, "logs"),
            EnableConsoleSink: false,
            EnableFileSink: false));
        _resilience = services.BuildServiceProvider().GetRequiredService<IResilienceExecutor>();
        _store = new JsonWorkspaceStateStore(CodeViewerStorageRoot.InMemory, _resilience);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private ProjectTabViewModel CreateTab(string path) => new(
        path,
        new ProjectTreeEnumerator(_resilience),
        new ProjectFileReader(_resilience),
        _store,
        copyToClipboard: _ => System.Threading.Tasks.Task.CompletedTask,
        uiDispatcher: new InlineUiDispatcher());

    // Build on disk: root/src/main.py, root/src/util.py, root/README.md
    private string BuildProjectOnDisk()
    {
        var project = Path.Combine(_root, "Demo");
        Directory.CreateDirectory(Path.Combine(project, "src"));
        File.WriteAllText(Path.Combine(project, "src", "main.py"), "print(1)\n");
        File.WriteAllText(Path.Combine(project, "src", "util.py"), "x = 1\n");
        File.WriteAllText(Path.Combine(project, "README.md"), "hi\n");
        return project;
    }

    [Fact]
    public async Task SaveThenRestoreRoundTripsSelectionsAndExpansion()
    {
        var project = BuildProjectOnDisk();

        // Open, expand src, check main.py, save.
        var tab = CreateTab(project);
        await tab.ToggleExpand(tab.State.Root);
        // root now loaded; expand src
        var src = tab.State.Root.Children.Single(c => c.Name == "src");
        await tab.ToggleExpand(src);
        var main = src.Children.Single(c => c.Name == "main.py");
        tab.ToggleCheck(main);
        await tab.SaveAsync();

        // New tab, restore.
        var restored = CreateTab(project);
        await restored.RestoreAsync();

        restored.State.Root.IsExpanded.Should().BeTrue();
        var restoredSrc = restored.State.Root.Children.Single(c => c.Name == "src");
        restoredSrc.IsExpanded.Should().BeTrue();
        var restoredMain = restoredSrc.Children.Single(c => c.Name == "main.py");
        restoredMain.Selection.Should().Be(NodeSelectionState.Checked);
        // util.py was not checked -> stays unchecked.
        restoredSrc.Children.Single(c => c.Name == "util.py")
            .Selection.Should().Be(NodeSelectionState.Unchecked);
    }

    [Fact]
    public async Task RestoreDoesNotSelectNewlyDiscoveredFiles()
    {
        var project = BuildProjectOnDisk();

        var tab = CreateTab(project);
        await tab.ToggleExpand(tab.State.Root);
        var src = tab.State.Root.Children.Single(c => c.Name == "src");
        await tab.ToggleExpand(src);
        var main = src.Children.Single(c => c.Name == "main.py");
        tab.ToggleCheck(main);
        await tab.SaveAsync();

        // Add a NEW file after saving.
        File.WriteAllText(Path.Combine(project, "src", "new.py"), "new\n");

        var restored = CreateTab(project);
        await restored.RestoreAsync();

        var restoredSrc = restored.State.Root.Children.Single(c => c.Name == "src");
        // new.py discovered during targeted enumeration but NOT checked.
        restoredSrc.Children.Single(c => c.Name == "new.py")
            .Selection.Should().Be(NodeSelectionState.Unchecked);
        restoredSrc.Children.Single(c => c.Name == "main.py")
            .Selection.Should().Be(NodeSelectionState.Checked);
    }

    [Fact]
    public async Task RestoreWithAutoDisplayOnDoesNotFullScan()
    {
        var project = BuildProjectOnDisk();

        var tab = CreateTab(project);
        await tab.ToggleExpand(tab.State.Root);
        var src = tab.State.Root.Children.Single(c => c.Name == "src");
        await tab.ToggleExpand(src);
        var main = src.Children.Single(c => c.Name == "main.py");
        tab.ToggleCheck(main);
        // Simulate Auto Display ON saved state.
        tab.State.RestoreAutoDisplayFlag();
        await tab.SaveAsync();

        var restored = CreateTab(project);
        await restored.RestoreAsync();

        // Auto Display flag restored, but only the saved selection is
        // checked - NOT the whole project.
        restored.AutoDisplay.Should().BeTrue();
        restored.State.GetSelectedFiles().Should().ContainSingle(f => f.Name == "main.py");
    }

    [Fact]
    public async Task RestoreMissingPathsAreDropped()
    {
        var project = BuildProjectOnDisk();

        var tab = CreateTab(project);
        await tab.ToggleExpand(tab.State.Root);
        var src = tab.State.Root.Children.Single(c => c.Name == "src");
        await tab.ToggleExpand(src);
        var main = src.Children.Single(c => c.Name == "main.py");
        tab.ToggleCheck(main);
        await tab.SaveAsync();

        // Delete the checked file while "closed".
        File.Delete(Path.Combine(project, "src", "main.py"));

        var restored = CreateTab(project);
        var act = async () => await restored.RestoreAsync();

        await act.Should().NotThrowAsync();
        restored.State.GetSelectedFiles().Should().BeEmpty();
    }
}
