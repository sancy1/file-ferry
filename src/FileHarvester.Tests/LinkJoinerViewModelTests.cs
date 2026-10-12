using FileHarvester.App.ViewModels;
using FluentAssertions;

namespace FileHarvester.Tests;

public sealed class LinkJoinerViewModelTests
{
    [Fact]
    public void JoinAndSectionCommandsUpdatePreviewAndCounts()
    {
        var viewModel = new LinkJoinerViewModel();
        var section = viewModel.Sections.Single();
        section.BaseLocation = @"C:\Project";
        section.Paths = @"src\App.cs
docs\guide.md";

        viewModel.InputCount.Should().Be(2);
        viewModel.ResultCount.Should().Be(2);
        viewModel.ResultsText.Should().Be(
            @"C:\Project\src\App.cs" + Environment.NewLine
            + @"C:\Project\docs\guide.md");

        viewModel.AddSectionCommand.Execute(null);
        viewModel.Sections.Should().HaveCount(2);
        viewModel.Sections[1].Name.Should().Be("Section 2");
    }

    [Fact]
    public void BrowseCommandsRaiseSectionSpecificHandoffs()
    {
        var viewModel = new LinkJoinerViewModel();
        var section = viewModel.Sections.Single();
        LinkJoinerSectionViewModel? folderRequested = null;
        LinkJoinerSectionViewModel? filesRequested = null;
        viewModel.BrowseBaseRequested += (_, requested) => folderRequested = requested;
        viewModel.AddFilesRequested += (_, requested) => filesRequested = requested;

        section.BrowseBaseCommand.Execute(null);
        section.AddFilesCommand.Execute(null);

        folderRequested.Should().BeSameAs(section);
        filesRequested.Should().BeSameAs(section);
    }

    [Fact]
    public void SendToHarvestRaisesResultsOnlyWithoutParsingOrResolving()
    {
        var viewModel = new LinkJoinerViewModel();
        var section = viewModel.Sections.Single();
        section.BaseLocation = @"D:\Archive";
        section.Paths = @"2025\report.pdf";
        string? sent = null;
        viewModel.SendToHarvestRequested += (_, text) => sent = text;

        viewModel.SendToHarvestCommand.CanExecute(null).Should().BeTrue();
        viewModel.SendToHarvestCommand.Execute(null);

        sent.Should().Be(@"D:\Archive\2025\report.pdf");
    }

    [Fact]
    public void CopyAndSaveCommandsRaiseHandoffsWithOnlyJoinedResults()
    {
        var viewModel = new LinkJoinerViewModel();
        var section = viewModel.Sections.Single();
        section.BaseLocation = @"C:\Project";
        section.Paths = "src\\App.cs\n\n..\\outside.txt";
        string? copied = null;
        string? saved = null;
        viewModel.CopyRequested += (_, text) => copied = text;
        viewModel.SaveRequested += (_, text) => saved = text;

        viewModel.CopyAllCommand.CanExecute(null).Should().BeTrue();
        viewModel.SaveResultsCommand.CanExecute(null).Should().BeTrue();
        viewModel.CopyAllCommand.Execute(null);
        viewModel.SaveResultsCommand.Execute(null);

        copied.Should().Be(@"C:\Project\src\App.cs");
        saved.Should().Be(copied);
        viewModel.AttentionCount.Should().Be(2);
    }

    [Fact]
    public void ClearResetsContentAndRemainsWithOneSection()
    {
        var viewModel = new LinkJoinerViewModel();
        viewModel.Sections.Single().Paths = "relative.txt";

        viewModel.ClearCommand.Execute(null);

        viewModel.Sections.Should().ContainSingle();
        viewModel.ResultsText.Should().BeEmpty();
    }
}
