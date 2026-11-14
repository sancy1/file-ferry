// ============================================================================
// FILE:     src/FileHarvester.Tests/CodeViewer/CodeViewerViewModelTests.cs
// PURPOSE:  Tests for the CodeViewer tab strip: opening projects, tab
//           reuse for the same folder, per-tab state isolation, and close
//           behaviour. Uses real services against temp directories.
// ============================================================================

using CodeViewer.Platform.FileSystem;
using CodeViewer.Platform.State;
using FileHarvester.App.ViewModels;
using FileHarvester.Core.Composition;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.Tests.CodeViewer;

public sealed class CodeViewerViewModelTests
{
    private static CodeViewerViewModel CreateViewModel()
    {
        var services = new ServiceCollection();
        services.AddFileHarvester(new ResilienceConfig(
            LogDirectory: Path.Combine(Path.GetTempPath(), "cv-vm-logs"),
            EnableConsoleSink: false,
            EnableFileSink: false));
        var provider = services.BuildServiceProvider();
        var resilience = provider.GetRequiredService<IResilienceExecutor>();

        return new CodeViewerViewModel(
            new ProjectTreeEnumerator(resilience),
            new ProjectFileReader(resilience),
            new JsonWorkspaceStateStore(CodeViewerStorageRoot.InMemory, resilience),
            copyToClipboard: _ => System.Threading.Tasks.Task.CompletedTask);
    }

    [Fact]
    public void OpeningProjectCreatesTabAndActivatesIt()
    {
        var vm = CreateViewModel();

        vm.OpenProject(@"C:\Projects\Demo");

        vm.Tabs.Should().HaveCount(1);
        vm.ActiveTab.Should().NotBeNull();
        vm.HasActiveTab.Should().BeTrue();
        vm.ShowEmptyState.Should().BeFalse();
    }

    [Fact]
    public void OpeningSameFolderTwiceReusesTab()
    {
        var vm = CreateViewModel();
        vm.OpenProject(@"C:\Projects\Demo");

        vm.OpenProject(@"c:\projects\demo\");

        vm.Tabs.Should().HaveCount(1);
    }

    [Fact]
    public void EachTabHasIndependentState()
    {
        var vm = CreateViewModel();
        vm.OpenProject(@"C:\Projects\A");
        vm.OpenProject(@"C:\Projects\B");

        vm.Tabs.Should().HaveCount(2);
        vm.Tabs[0].State.Should().NotBeSameAs(vm.Tabs[1].State);
        vm.Tabs[0].ProjectPath.Should().NotBe(vm.Tabs[1].ProjectPath);
    }

    [Fact]
    public void ClosingActiveTabActivatesNeighbour()
    {
        var vm = CreateViewModel();
        vm.OpenProject(@"C:\Projects\A");
        vm.OpenProject(@"C:\Projects\B");
        vm.OpenProject(@"C:\Projects\C");
        var middle = vm.Tabs[1];
        vm.ActiveTab = middle;

        vm.CloseTab(middle);

        vm.Tabs.Should().HaveCount(2);
        vm.ActiveTab.Should().NotBeNull();
        vm.ActiveTab.Should().NotBeSameAs(middle);
    }

    [Fact]
    public void ClosingLastTabShowsEmptyState()
    {
        var vm = CreateViewModel();
        vm.OpenProject(@"C:\Projects\Demo");
        vm.CloseTab(vm.Tabs[0]);

        vm.Tabs.Should().BeEmpty();
        vm.ActiveTab.Should().BeNull();
        vm.ShowEmptyState.Should().BeTrue();
    }

    [Fact]
    public void NewTabStartsWithAutoDisplayOffAndRootCollapsed()
    {
        var vm = CreateViewModel();
        vm.OpenProject(@"C:\Projects\Demo");

        var tab = vm.Tabs[0];
        tab.AutoDisplay.Should().BeFalse();
        tab.State.Root.IsExpanded.Should().BeFalse();
        tab.State.Root.Selection.Should().Be(global::CodeViewer.Core.Domain.NodeSelectionState.Unchecked);
        tab.BrowserRows.Should().HaveCount(1); // just the root row
    }
}
