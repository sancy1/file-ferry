// ============================================================================
// FILE:     src/FileHarvester.Tests/Prompt/ViewModel/PromptShortcutsTests.cs
// PURPOSE:  Proves the P5.2 keyboard-shortcut commands and the shortcuts dialog VM.
// ============================================================================
using FileHarvester.App.ViewModels;
using NSubstitute;
using Prompt.Core.Domain;
using Xunit;
namespace FileHarvester.Tests.Prompt.ViewModel;

/// <summary>Proves the P5.2 shortcut commands and the shortcuts dialog VM.</summary>
public sealed class PromptShortcutsTests
{
    /// <summary>T1: page VM's FocusSearchCommand raises FocusSearchRequested.</summary>
    [Fact]
    public void FocusSearchCommandRaisesFocusSearchRequested()
    {
        var page = new PromptPageViewModel(Substitute.For<IPromptStore>(), Substitute.For<ICategoryStore>(), Substitute.For<ISubCategoryStore>());
        var raised = false;
        page.FocusSearchRequested += (_, _) => raised = true;
        page.FocusSearchCommand.Execute(null);
        Assert.True(raised);
    }

    /// <summary>T2: viewer VM's ShortcutsCommand raises ShortcutsRequested.</summary>
    [Fact]
    public void ShortcutsCommandRaisesShortcutsRequested()
    {
        var viewer = new PromptViewerViewModel(Substitute.For<IPromptStore>());
        var raised = false;
        viewer.ShortcutsRequested += (_, _) => raised = true;
        viewer.ShortcutsCommand.Execute(null);
        Assert.True(raised);
    }

    /// <summary>T3: the shortcuts dialog VM lists the six expected shortcuts.</summary>
    [Fact]
    public void ShortcutsDialogListsSixShortcuts()
    {
        var vm = new PromptShortcutsDialogViewModel();
        Assert.Equal(6, vm.Rows.Count);
        Assert.Contains(vm.Rows, r => r.Keys == "Ctrl+N");
        Assert.Contains(vm.Rows, r => r.Keys == "Ctrl+F");
        Assert.Contains(vm.Rows, r => r.Keys == "Ctrl+E");
        Assert.Contains(vm.Rows, r => r.Keys == "Delete");
        Assert.Contains(vm.Rows, r => r.Keys == "Ctrl+S");
        Assert.Contains(vm.Rows, r => r.Keys == "Ctrl+Z / Ctrl+Y");
        Assert.All(vm.Rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Description)));
    }

    /// <summary>T4: the shortcuts dialog VM's CloseCommand raises CloseRequested.</summary>
    [Fact]
    public void ShortcutsDialogCloseCommandRaisesCloseRequested()
    {
        var vm = new PromptShortcutsDialogViewModel();
        var raised = false;
        vm.CloseRequested += (_, _) => raised = true;
        vm.CloseCommand.Execute(null);
        Assert.True(raised);
    }
}