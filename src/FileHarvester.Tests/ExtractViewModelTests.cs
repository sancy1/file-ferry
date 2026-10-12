using FileHarvester.App.ViewModels;
using FluentAssertions;

namespace FileHarvester.Tests;

public sealed class ExtractViewModelTests
{
    [Fact]
    public void ExtractCommandsSetCategoryFilterAndCounts()
    {
        var viewModel = new ExtractViewModel
        {
            InputText = "a.txt name+tag@example.com 08031234567",
        };

        viewModel.ExtractAllCommand.Execute(null);
        viewModel.AllCount.Should().Be(3);
        viewModel.VisibleItems.Should().HaveCount(3);

        viewModel.ExtractEmailsCommand.Execute(null);
        viewModel.ActiveFilter.Should().Be(ExtractResultFilter.Emails);
        viewModel.VisibleItems.Select(item => item.Value)
            .Should().Equal("name+tag@example.com");

        viewModel.ExtractLinksCommand.Execute(null);
        viewModel.ActiveFilter.Should().Be(ExtractResultFilter.LinksAndPaths);
        viewModel.VisibleItems.Select(item => item.Value).Should().Equal("a.txt");

        viewModel.ExtractPhonesCommand.Execute(null);
        viewModel.VisibleItems.Select(item => item.Value).Should().Equal("08031234567");
    }

    [Fact]
    public void CopyAllAndActiveCategoryAreSeparateAndPreserveOrder()
    {
        var viewModel = new ExtractViewModel
        {
            InputText = "a.txt name@example.com 08031234567",
        };
        viewModel.ExtractAllCommand.Execute(null);
        viewModel.CopyActiveCategoryCommand.CanExecute(null).Should().BeFalse();

        string? active = null;
        string? all = null;
        viewModel.CopyRequested += (_, value) =>
        {
            if (active is null)
            {
                active = value;
            }
            else
            {
                all = value;
            }
        };

        viewModel.SelectFilter(ExtractResultFilter.Emails);
        viewModel.CopyActiveCategoryCommand.Execute(null);
        viewModel.CopyAllCommand.Execute(null);

        active.Should().Be("name@example.com");
        all.Should().Be("a.txt" + Environment.NewLine
            + "name@example.com" + Environment.NewLine
            + "08031234567");
    }

    [Fact]
    public void CopySelectedAndPerItemCopyRaiseClipboardHandoffs()
    {
        var viewModel = new ExtractViewModel
        {
            InputText = "a.txt b.pdf",
        };
        viewModel.ExtractAllCommand.Execute(null);
        var copied = new List<string>();
        viewModel.CopyRequested += (_, value) => copied.Add(value);

        viewModel.CopySelected(viewModel.VisibleItems);
        viewModel.VisibleItems[0].CopyCommand.Execute(null);

        copied.Should().Equal(
            "a.txt" + Environment.NewLine + "b.pdf",
            "a.txt");
    }

    [Fact]
    public void SaveHandoffOptionallyIncludesCategoryHeadingsAndCounts()
    {
        var viewModel = new ExtractViewModel
        {
            InputText = "a.txt name@example.com",
        };
        viewModel.ExtractAllCommand.Execute(null);
        string? saved = null;
        viewModel.SaveRequested += (_, value) => saved = value;

        viewModel.SaveResultsCommand.Execute(null);

        saved.Should().Contain("Links & Paths (1)")
            .And.Contain("Emails (1)")
            .And.Contain("Phones (0)")
            .And.Contain("(No matches)");

        viewModel.IncludeCategoryHeadings = false;
        viewModel.SaveResultsCommand.Execute(null);
        saved.Should().Be("a.txt" + Environment.NewLine + "name@example.com");
    }

    [Fact]
    public void PasteHandoffAndCharacterLineCountsAreReported()
    {
        var viewModel = new ExtractViewModel();
        var pasteRequested = false;
        viewModel.PasteRequested += (_, _) => pasteRequested = true;
        viewModel.PasteCommand.Execute(null);
        pasteRequested.Should().BeTrue();

        viewModel.InputText = "first line\r\nsecond line";
        viewModel.CharacterCount.Should().Be("first line\r\nsecond line".Length);
        viewModel.LineCount.Should().Be(2);

        viewModel.SetInputText("pasted text");

        viewModel.InputText.Should().Be("pasted text");
        viewModel.StatusText.Should().Contain("pasted");
    }

    [Fact]
    public void CloseCommandRaisesNavigationHandoff()
    {
        var viewModel = new ExtractViewModel();
        var closeRequested = false;
        viewModel.CloseRequested += (_, _) => closeRequested = true;

        viewModel.CloseCommand.Execute(null);

        closeRequested.Should().BeTrue();
    }

    [Fact]
    public void EditingInputDisablesCopyAndSaveUntilExtractedAgain()
    {
        var viewModel = new ExtractViewModel
        {
            InputText = "a.txt",
        };
        viewModel.ExtractAllCommand.Execute(null);
        viewModel.CopyAllCommand.CanExecute(null).Should().BeTrue();
        viewModel.SaveResultsCommand.CanExecute(null).Should().BeTrue();

        viewModel.InputText = "b.pdf";

        viewModel.CopyAllCommand.CanExecute(null).Should().BeFalse();
        viewModel.SaveResultsCommand.CanExecute(null).Should().BeFalse();
        viewModel.VisibleItems.Single().CopyCommand.CanExecute(null).Should().BeFalse();
        viewModel.StatusText.Should().Contain("Input changed");
    }

    [Fact]
    public void ClearInputAndClearResultsHaveIndependentBehavior()
    {
        var viewModel = new ExtractViewModel
        {
            InputText = "a.txt",
        };
        viewModel.ExtractAllCommand.Execute(null);

        viewModel.ClearInputCommand.Execute(null);
        viewModel.InputText.Should().BeEmpty();
        viewModel.AllCount.Should().Be(1);
        viewModel.CopyAllCommand.CanExecute(null).Should().BeFalse();

        viewModel.ClearResultsCommand.Execute(null);
        viewModel.AllCount.Should().Be(0);
        viewModel.VisibleItems.Should().BeEmpty();
    }

    [Fact]
    public void EmptyInputAndNoMatchesShowClearStatus()
    {
        var viewModel = new ExtractViewModel();
        viewModel.ExtractAllCommand.Execute(null);
        viewModel.StatusText.Should().Contain("before extracting");

        viewModel.InputText = "ordinary prose";
        viewModel.ExtractAllCommand.Execute(null);
        viewModel.StatusText.Should().Be("No matches found.");
        viewModel.VisibleItems.Should().BeEmpty();
    }
}
