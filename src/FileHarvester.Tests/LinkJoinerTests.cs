using FileHarvester.Core.Domain;
using FluentAssertions;

namespace FileHarvester.Tests;

public sealed class LinkJoinerTests
{
    [Fact]
    public void AppliesOneBaseToMultiplePathsInOrder()
    {
        var result = LinkJoiner.Join(new[]
        {
            new LinkJoinerSectionInput(
                "Project", @"C:\Project", "src\\App.cs\ndocs/guide.md\nassets/logo.png"),
        });

        result.Results.Should().Equal(
            @"C:\Project\src\App.cs",
            @"C:\Project\docs\guide.md",
            @"C:\Project\assets\logo.png");
        result.InputCount.Should().Be(3);
        result.ResultCount.Should().Be(3);
    }

    [Fact]
    public void SupportsMultipleBasesAndPreservesSectionOrder()
    {
        var result = LinkJoiner.Join(new[]
        {
            new LinkJoinerSectionInput("Project", @"C:\Project", @"src\App.cs"),
            new LinkJoinerSectionInput("Archive", @"D:\Archive\", @"2025\report.pdf"),
        });

        result.Results.Should().Equal(
            @"C:\Project\src\App.cs",
            @"D:\Archive\2025\report.pdf");
    }

    [Theory]
    [InlineData(@"C:\absolute\file.txt")]
    [InlineData(@"\\server\share\file.txt")]
    [InlineData("/home/user/file.txt")]
    [InlineData("gdrive://FILE_ID")]
    [InlineData("s3://bucket/key")]
    public void AbsolutePathsAndUrisPassThrough(string input)
    {
        var result = LinkJoiner.Join(new[]
        {
            new LinkJoinerSectionInput("Section", "not-used", input),
        });

        result.Results.Should().ContainSingle().Which.Should().Be(input);
        result.Rows[0].Kind.Should().Be(LinkJoinerRowKind.PassThrough);
    }

    [Theory]
    [InlineData(@"C:\Project", @"src\App.cs", @"C:\Project\src\App.cs")]
    [InlineData(@"C:\Project\", @"src\App.cs", @"C:\Project\src\App.cs")]
    [InlineData("/home/project", "src/app.cs", "/home/project/src/app.cs")]
    [InlineData(@"\\server\share", @"docs\guide.md", @"\\server\share\docs\guide.md")]
    public void HandlesBaseRootsAndTrailingSeparators(
        string basePath,
        string relativePath,
        string expected)
    {
        var result = LinkJoiner.Join(new[]
        {
            new LinkJoinerSectionInput("Section", basePath, relativePath),
        });

        result.Results.Should().ContainSingle().Which.Should().Be(expected);
    }

    [Fact]
    public void DoesNotDuplicateAnAlreadyPrefixedBase()
    {
        var result = LinkJoiner.Join(new[]
        {
            new LinkJoinerSectionInput("Section", @"C:\Project", @"C:\Project\src\App.cs"),
        });

        result.Results.Should().ContainSingle().Which.Should().Be(@"C:\Project\src\App.cs");
        result.Rows[0].Kind.Should().Be(LinkJoinerRowKind.AlreadyPrefixed);
    }

    [Fact]
    public void PreserveSeparatorsOptionKeepsEnteredStyle()
    {
        var result = LinkJoiner.Join(new[]
        {
            new LinkJoinerSectionInput(
                "Section", @"C:\Project\", @"src/mixed\\file.txt", NormalizeSeparators: false),
        });

        result.Results.Should().ContainSingle().Which.Should().Be(@"C:\Project\src/mixed\\file.txt");
    }

    [Fact]
    public void ReportsBlankInvalidAndTraversalRowsWithoutJoiningThem()
    {
        var result = LinkJoiner.Join(new[]
        {
            new LinkJoinerSectionInput(
                "Section", @"C:\Project", @"src\file.txt

..\outside.txt
C:relative.txt
"),
        });

        result.Results.Should().ContainSingle().Which.Should().Be(@"C:\Project\src\file.txt");
        result.Rows.Select(row => row.Kind).Should().Equal(
            LinkJoinerRowKind.Joined,
            LinkJoinerRowKind.Blank,
            LinkJoinerRowKind.Invalid,
            LinkJoinerRowKind.Invalid,
            LinkJoinerRowKind.Blank);
        result.AttentionCount.Should().Be(4);
        result.Rows[2].Message.Should().Contain("traverses above");
        result.Rows[3].Message.Should().Contain("Drive-relative");
    }

    [Fact]
    public void PreservesDuplicatesByDefaultAndOnlyRemovesWhenRequested()
    {
        var section = new LinkJoinerSectionInput("Section", @"C:\Project", "a.txt\na.txt");
        var defaultResult = LinkJoiner.Join(new[] { section });
        var deduplicated = LinkJoiner.Join(new[] { section }, removeDuplicates: true);

        defaultResult.Results.Should().HaveCount(2);
        deduplicated.Results.Should().ContainSingle().Which.Should().Be(@"C:\Project\a.txt");
        deduplicated.Rows[1].Kind.Should().Be(LinkJoinerRowKind.Duplicate);
    }

    [Fact]
    public void RelativePathWithoutAbsoluteBaseIsReportedInvalid()
    {
        var result = LinkJoiner.Join(new[]
        {
            new LinkJoinerSectionInput("Section", "relative-base", "file.txt"),
        });

        result.Results.Should().BeEmpty();
        result.Rows.Single().Kind.Should().Be(LinkJoinerRowKind.Invalid);
        result.Rows.Single().Message.Should().Contain("absolute local base");
    }
}
