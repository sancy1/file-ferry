using FileHarvester.Core.Domain;
using FluentAssertions;

namespace FileHarvester.Tests;

public sealed class ExtractorTests
{
    [Fact]
    public void ExtractsLinksPathsAndBareFilenamesInFirstSeenOrder()
    {
        const string text =
            "See https://Example.com/docs?q=a#b). file://C:/work/a.txt, "
            + @"C:\Projects\notification_handler.go, \\server\share\report.pdf; "
            + "//server/share/public/info.txt, /var/log/app.txt, docs/guide.md "
            + "and notification_handler.go.";

        var result = Extractor.Extract(text);

        result.LinksAndPaths.Select(item => item.Value).Should().Equal(
            "https://Example.com/docs?q=a#b",
            "file://C:/work/a.txt",
            @"C:\Projects\notification_handler.go",
            @"\\server\share\report.pdf",
            "//server/share/public/info.txt",
            "/var/log/app.txt",
            "docs/guide.md",
            "notification_handler.go");
        result.LinksAndPaths.Select(item => item.StartIndex)
            .Should().BeInAscendingOrder();
    }

    [Theory]
    [InlineData("http://example.test/path", "http://example.test/path")]
    [InlineData("https://example.test/a_(b).", "https://example.test/a_(b)")]
    [InlineData("ftp://files.example.test/a.zip,", "ftp://files.example.test/a.zip")]
    [InlineData("file:///tmp/report.pdf;", "file:///tmp/report.pdf")]
    public void TrimsOnlyObviousSurroundingPunctuation(string input, string expected)
    {
        var result = Extractor.Extract(input);

        result.LinksAndPaths.Should().ContainSingle()
            .Which.Value.Should().Be(expected);
    }

    [Fact]
    public void ExtractsCommonEmailFormsAndDeduplicatesIgnoringCase()
    {
        var result = Extractor.Extract(
            "Contact Name+tag@example.com, name+tag@EXAMPLE.com and "
            + "foo@bar, bad..dots@example.com, .leading@example.com.");

        result.Emails.Select(item => item.Value).Should()
            .Equal("Name+tag@example.com");
    }

    [Fact]
    public void ExtractsPhoneLikeMatchesWithoutRewritingOrDeduplicating()
    {
        const string first = "+234 803 123 4567";
        const string text = first + ", " + first
            + "; +1-800-555-1234; (555) 123-4567; 08031234567";

        var result = Extractor.Extract(text);

        result.Phones.Select(item => item.Value).Should().Equal(
            first,
            first,
            "+1-800-555-1234",
            "(555) 123-4567",
            "08031234567");
    }

    [Fact]
    public void DoesNotTreatDatesAddressesOrEmbeddedPathContentAsPhones()
    {
        var result = Extractor.Extract(
            "Date 2025-10-11, IP 192.168.0.1, mail user1234567@example.com, "
            + @"path C:\Archive\1234567890.txt");

        result.Phones.Should().BeEmpty();
    }

    [Fact]
    public void FindsPhoneMatchesInInputOrder()
    {
        var result = Extractor.Extract(
            "First 08031234567, then +1-800-555-1234.");

        result.Phones.Select(item => item.Value).Should()
            .Equal("08031234567", "+1-800-555-1234");
        result.Phones.Select(item => item.StartIndex)
            .Should().BeInAscendingOrder();
    }

    [Fact]
    public void ReturnsEmptyCategoriesForEmptyOrUnmatchedInput()
    {
        Extractor.Extract(string.Empty).All.Should().BeEmpty();
        Extractor.Extract("ordinary prose without matches").All.Should().BeEmpty();
    }

    [Fact]
    public void DeduplicatesPathsCaseSensitivelyAndKeepsOriginalMatchPositions()
    {
        var result = Extractor.Extract(
            "a.txt A.txt a.txt https://example.test https://example.test");

        result.LinksAndPaths.Select(item => item.Value).Should()
            .Equal("a.txt", "A.txt", "https://example.test");
        result.LinksAndPaths[1].StartIndex.Should().Be("a.txt ".Length);
    }
}
