// filepath: src/FileHarvester.Tests/ImageTextify/OcrResultMergerTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 2.4b2
// purpose:  Behavior tests for OcrResultMerger. Verifies reading order,
//           empty line dropping, confidence computation, and OcrTextKind
//           determination. Pure logic, no ONNX.
//
// RELATIONSHIPS
//   Implements : n/a (test file)
//   Depends on : ImageTextify.Platform.Imaging.OcrResultMerger
//   Used by    : none found (test runner)
//   See also   : OcrResultMerger.cs
//
// NOTES
//   - Uses OcrLineResult directly. No model runner needed.

using FluentAssertions;
using ImageTextify.Core.Abstractions;
using ImageTextify.Platform.Imaging;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Tests for <see cref="OcrResultMerger"/>.
/// </summary>
public sealed class OcrResultMergerTests
{
    private static OcrLineResult Line(
        string text, int x, int y, int w = 100, int h = 20,
        double? conf = 0.9, OcrEngineKind engine = OcrEngineKind.Ppocr) =>
        new(text, new OcrBoundingBox(x, y, w, h), conf, engine);

    /// <summary>
    /// Empty input produces empty result with Unknown kind.
    /// </summary>
    [Fact]
    public void EmptyInputProducesEmptyResult()
    {
        var merger = new OcrResultMerger();
        var result = merger.Merge(Array.Empty<OcrLineResult>(), Array.Empty<string>());

        result.Text.Should().BeEmpty();
        result.Confidence.Should().BeNull();
        result.DetectedTextKind.Should().Be(OcrTextKind.Unknown);
        result.Lines.Should().BeEmpty();
    }

    /// <summary>
    /// Empty text lines are dropped.
    /// </summary>
    [Fact]
    public void EmptyTextLinesAreDropped()
    {
        var merger = new OcrResultMerger();
        var lines = new[]
        {
            Line("", 0, 0),
            Line("Hello", 0, 40),
            Line("   ", 0, 80),
        };

        var result = merger.Merge(lines, Array.Empty<string>());

        result.Text.Should().Be("Hello");
        result.Lines.Should().HaveCount(1);
    }

    /// <summary>
    /// Two-column layout: left column before right column.
    /// </summary>
    [Fact]
    public void TwoColumnLayoutReturnsLeftColumnFirst()
    {
        var merger = new OcrResultMerger();
        var leftTop = Line("L-top", 0, 0);
        var leftBottom = Line("L-bottom", 0, 40);
        var rightTop = Line("R-top", 300, 0);
        var rightBottom = Line("R-bottom", 300, 40);

        var result = merger.Merge(
            new[] { rightTop, leftTop, rightBottom, leftBottom },
            Array.Empty<string>());

        result.Lines.Select(l => l.Text).Should().ContainInOrder(
            "L-top", "L-bottom", "R-top", "R-bottom");
    }

    /// <summary>
    /// Mixed engines produce OcrTextKind.Mixed.
    /// </summary>
    [Fact]
    public void MixedEnginesProduceMixedKind()
    {
        var merger = new OcrResultMerger();
        var lines = new[]
        {
            Line("Printed", 0, 0, engine: OcrEngineKind.Ppocr),
            Line("Handwritten", 0, 40, engine: OcrEngineKind.Trocr),
        };

        var result = merger.Merge(lines, Array.Empty<string>());

        result.DetectedTextKind.Should().Be(OcrTextKind.Mixed);
    }

    /// <summary>
    /// All-TrOCR engines produce Handwritten kind.
    /// </summary>
    [Fact]
    public void AllTrocrEnginesProduceHandwrittenKind()
    {
        var merger = new OcrResultMerger();
        var lines = new[]
        {
            Line("Line one", 0, 0, engine: OcrEngineKind.Trocr),
            Line("Line two", 0, 40, engine: OcrEngineKind.TrocrWholeImage),
        };

        var result = merger.Merge(lines, Array.Empty<string>());

        result.DetectedTextKind.Should().Be(OcrTextKind.Handwritten);
    }

    /// <summary>
    /// Overall confidence is mean of non-null per-line confidences.
    /// </summary>
    [Fact]
    public void OverallConfidenceIsMeanOfNonNullLines()
    {
        var merger = new OcrResultMerger();
        var lines = new[]
        {
            Line("A", 0, 0, conf: 0.8),
            Line("B", 0, 40, conf: 0.6),
            Line("C", 0, 80, conf: null, engine: OcrEngineKind.Trocr),
        };

        var result = merger.Merge(lines, Array.Empty<string>());

        result.Confidence.Should().BeApproximately(0.7, 0.001);
    }

    /// <summary>
    /// A paragraph-sized vertical gap now produces a blank line between
    /// paragraphs. The left indent is still dropped because the merger
    /// ignores X for indentation.
    /// </summary>
    [Fact]
    public void ParagraphGapProducesBlankLine()
    {
        var merger = new OcrResultMerger();
        var lines = new[]
        {
            Line("Para one line one", 10, 0),
            Line("Para one line two", 10, 20),
            Line("Para two line one", 10, 80),
            Line("Indented line", 30, 100),
        };
        var result = merger.Merge(lines, Array.Empty<string>());
        result.Text.Should().Be("Para one line one Para one line two" + "\n\n" + "Para two line one Indented line");
    }

    /// <summary>
    /// Two lines close together vertically stay in one paragraph; a large
    /// vertical gap starts a new paragraph.
    /// </summary>
    [Fact]
    public void CloseLinesShareParagraphDistantLinesSplit()
    {
        var merger = new OcrResultMerger();
        var lines = new[]
        {
            Line("First paragraph line one", 10, 0, w: 300),
            Line("First paragraph line two", 10, 20, w: 300),
            Line("Second paragraph", 10, 120, w: 300),
        };
        var result = merger.Merge(lines, Array.Empty<string>());
        result.Text.Should().Be("First paragraph line one First paragraph line two" + "\n\n" + "Second paragraph");
    }

    /// <summary>
    /// A short previous line ends the paragraph even when the gap is small.
    /// </summary>
    [Fact]
    public void ShortLineEndsParagraph()
    {
        var merger = new OcrResultMerger();
        var lines = new[]
        {
            Line("A full width line of text here", 10, 0, w: 300),
            Line("End.", 10, 20, w: 40),
            Line("Next paragraph starts", 10, 40, w: 300),
        };
        var result = merger.Merge(lines, Array.Empty<string>());
        result.Text.Should().Be("A full width line of text here End." + "\n\n" + "Next paragraph starts");
    }

    /// <summary>
    /// A row much taller than the body median is a heading: it is isolated
    /// with a blank line before and after.
    /// </summary>
    [Fact]
    public void TallRowIsIsolatedAsHeading()
    {
        var merger = new OcrResultMerger();
        var lines = new[]
        {
            Line("Body line before", 10, 0, w: 300, h: 20),
            Line("Big Heading", 10, 30, w: 300, h: 40),
            Line("Body line after", 10, 90, w: 300, h: 20),
        };
        var result = merger.Merge(lines, Array.Empty<string>());
        result.Text.Should().Be("Body line before" + "\n\n" + "Big Heading" + "\n\n" + "Body line after");
    }


    /// <summary>
    /// A single non-alphanumeric character is table border noise: it is
    /// dropped. A single letter or digit is kept.
    /// </summary>
    [Fact]
    public void PunctuationNoiseLinesAreDropped()
    {
        var merger = new OcrResultMerger();
        var lines = new[]
        {
            Line("A", 0, 0),
            Line("]", 0, 40),
            Line("|", 0, 80),
            Line("B", 0, 120),
        };
        var result = merger.Merge(lines, Array.Empty<string>());
        result.Text.Should().Be("A" + "\n\n" + "B");
    }
}
