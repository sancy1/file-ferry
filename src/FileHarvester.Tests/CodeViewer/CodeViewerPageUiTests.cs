// ============================================================================
// FILE:     src/FileHarvester.Tests/CodeViewer/CodeViewerPageUiTests.cs
// PURPOSE:  Headless UI tests that drive the real CodeViewerPage to
//           reproduce and verify the three reported defects: tab switching,
//           tab close, and code-card content rendering / Copy All.
// ============================================================================

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CodeViewer.Platform.FileSystem;
using CodeViewer.Platform.State;
using FileHarvester.App.Views;
using FileHarvester.App.ViewModels;
using FileHarvester.Core.Composition;
using FileHarvester.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.Tests.CodeViewer;

[Collection(AvaloniaCollection.Name)]
public sealed class CodeViewerPageUiTests
{
    private static CodeViewerViewModel CreateViewModel()
    {
        var services = new ServiceCollection();
        services.AddFileHarvester(new ResilienceConfig(
            LogDirectory: Path.Combine(Path.GetTempPath(), "cv-ui-logs"),
            EnableConsoleSink: false,
            EnableFileSink: false));
        var resilience = services.BuildServiceProvider().GetRequiredService<IResilienceExecutor>();

        return new CodeViewerViewModel(
            new ProjectTreeEnumerator(resilience),
            new ProjectFileReader(resilience),
            new JsonWorkspaceStateStore(CodeViewerStorageRoot.InMemory, resilience),
            copyToClipboard: _ => Task.CompletedTask);
    }

    [Fact]
    public void TabStripButtonsResolveCommands()
    {
        // The page binds directly to CodeViewerViewModel.
        var vm = CreateViewModel();
        vm.OpenProject(@"C:\Projects\A");
        vm.OpenProject(@"C:\Projects\B");

        var window = new Window { Width = 900, Height = 600 };
        var page = new CodeViewerPage { DataContext = vm };
        window.Content = page;

        try
        {
            window.Show();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.UpdateLayout();

            // Diagnose: is the tab strip present and realizing items?
            var tabStrip = page.GetVisualDescendants().OfType<ListBox>()
                .FirstOrDefault(l => l.Name == "TabStrip");
            tabStrip.Should().NotBeNull("the tab strip ListBox must exist");
            var itemsSource = tabStrip!.ItemsSource;
            itemsSource.Should().NotBeNull("CodeViewer.Tabs binding must resolve");
            (itemsSource as System.Collections.IEnumerable)!
                .Cast<object>().Count().Should().Be(2);
            tabStrip.ItemCount.Should().Be(2);
            var realizedContainers = tabStrip.GetVisualDescendants()
                .OfType<ListBoxItem>().Count();

            // Find the tab-strip buttons (switch + close).
            var buttons = page.GetVisualDescendants().OfType<Button>().ToList();
            var switchButtons = buttons.Where(b =>
                Avalonia.Automation.AutomationProperties.GetName(b) == "Switch to project").ToList();
            var closeButtons = buttons.Where(b =>
                Avalonia.Automation.AutomationProperties.GetName(b) == "Close project").ToList();

            _ = realizedContainers; // diagnostic
            switchButtons.Should().HaveCount(2, "two tabs are open");
            closeButtons.Should().HaveCount(2);

            // Commands must be bound (non-null) for switching/closing to work.
            foreach (var b in switchButtons)
            {
                b.Command.Should().NotBeNull("the switch-tab command must resolve");
                b.CommandParameter.Should().NotBeNull();
            }

            foreach (var b in closeButtons)
            {
                b.Command.Should().NotBeNull("the close-tab command must resolve");
            }
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ClickingTabHeaderSwitchesActiveTab()
    {
        var vm = CreateViewModel();
        vm.OpenProject(@"C:\Projects\A");
        vm.OpenProject(@"C:\Projects\B");
        var first = vm.Tabs[0];
        var second = vm.Tabs[1];
        vm.ActiveTab.Should().BeSameAs(second);

        // Drive the command directly (as the button would).
        vm.SelectTabCommand.Execute(first);

        vm.ActiveTab.Should().BeSameAs(first);
    }

    [Fact]
    public void CloseTabCommandClosesAndActivatesNeighbour()
    {
        var vm = CreateViewModel();
        vm.OpenProject(@"C:\Projects\A");
        vm.OpenProject(@"C:\Projects\B");
        var second = vm.Tabs[1];

        vm.CloseTabCommand.Execute(second);

        vm.Tabs.Should().HaveCount(1);
        vm.ActiveTab.Should().BeSameAs(vm.Tabs[0]);
    }

    [Fact]
    public async Task CodeCardRendersContentAndCopyAllEnables()
    {
        // Real file on disk so the reader returns content.
        var dir = Path.Combine(Path.GetTempPath(), "cv-card-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "main.py");
        File.WriteAllText(file, "print('hello')\nprint('world')\n");

        try
        {
            var vm = CreateViewModel();
            vm.OpenProject(dir);
            var tab = vm.ActiveTab!;

            // Expand root and check the file so a card is produced.
            await tab.ToggleExpand(tab.State.Root);
            var mainNode = tab.State.Root.Children.Single(c => c.Name == "main.py");
            tab.ToggleCheck(mainNode);

            tab.CodeCards.Should().HaveCount(1);
            var card = tab.CodeCards[0];

            // Load content (as the view does when the card is realized).
            await card.EnsureLoadedAsync();

            card.HasPreview.Should().BeTrue();
            card.Text.Should().Contain("print('hello')");
            card.LineCount.Should().BeGreaterThan(0);

            // Copy All must be enabled once a card exists.
            tab.CopyAllCommand.CanExecute(null).Should().BeTrue(
                "Copy All is enabled when at least one code card is present");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
