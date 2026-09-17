// ============================================================================
// FILE:     src/FileHarvester.Tests/BundleComposerTests.cs
// PURPOSE:  Unit tests for BundleComposer. Covers both output modes,
//           delimiter expansion, blank-line behavior, unreadable and
//           binary content handling, and empty input.
// RELATION: Consumes IBundleComposer and BundleComposer.RenderMergedText.
//           Proves the composer works before format writers (Phase 4) are
//           built on top of it.
// ============================================================================

using System.Text;
using FileHarvester.Core.Domain;
using FileHarvester.Core.Implementation;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests;

/// <summary>
/// Unit tests for <see cref="BundleComposer"/>.
/// </summary>
public sealed class BundleComposerTests
{
    private readonly BundleComposer _composer = new();

    // ---------------------------------------------------------------------
    // Helpers — build FileContent records without touching disk.
    // ---------------------------------------------------------------------

    private static FileContent MakeContent(
        string path,
        string text,
        int index = 0,
        bool isBinary = false,
        string? readError = null)
    {
        var bytes = Encoding.UTF8.GetBytes(text);

        var request = new FileRequest(Index: index, RawInput: path);
        var resolved = new ResolvedFile(
            Request:         request,
            AbsolutePath:    path,
            SizeBytes:       bytes.Length,
            LastModifiedUtc: new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc),
            Confidence:      ResolutionConfidence.Exact);

        return new FileContent(
            File:        resolved,
            RawBytes:    bytes,
            DecodedText: isBinary ? string.Empty : text,
            Encoding:    Encoding.UTF8,
            IsBinary:    isBinary,
            Sha256:      "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            ReadError:   readError);
    }

    // ---------------------------------------------------------------------
    // TEST 1 — SeparateFiles mode disables delimiter and blank lines
    // ---------------------------------------------------------------------
    [Fact]
    public void SeparateFilesModeDisablesDelimiterAndBlankLines()
    {
        var content = MakeContent("C:\\proj\\a.txt", "AAA");

        var document = _composer.Compose(
            new[] { content },
            ComposerOptions.SeparateFiles());

        document.Mode.Should().Be(OutputMode.SeparateFiles);
        document.DelimiterTemplate.Should().BeEmpty();
        document.TwoBlankLines.Should().BeFalse();
        document.Files.Should().HaveCount(1);
    }

    // ---------------------------------------------------------------------
    // TEST 2 — SingleMerged mode preserves delimiter and blank lines
    // ---------------------------------------------------------------------
    [Fact]
    public void SingleMergedModePreservesDelimiterAndBlankLines()
    {
        var content = MakeContent("C:\\proj\\a.txt", "AAA");

        var document = _composer.Compose(
            new[] { content },
            ComposerOptions.SingleMerged());

        document.Mode.Should().Be(OutputMode.SingleMerged);
        document.DelimiterTemplate.Should().Be(ComposedDocument.DefaultDelimiter);
        document.TwoBlankLines.Should().BeTrue();
    }

    // ---------------------------------------------------------------------
    // TEST 3 — RenderMergedText produces the inspiration-script shape
    // ---------------------------------------------------------------------
    [Fact]
    public void RenderMergedTextProducesInspirationShape()
    {
        var a = MakeContent("C:\\proj\\a.txt", "AAA");
        var b = MakeContent("C:\\proj\\b.txt", "BBB");

        var document = _composer.Compose(
            new[] { a, b },
            ComposerOptions.SingleMerged());

        var text = BundleComposer.RenderMergedText(document);

        text.Should().Contain("===== C:\\proj\\a.txt =====");
        text.Should().Contain("===== C:\\proj\\b.txt =====");
        text.Should().Contain("AAA");
        text.Should().Contain("BBB");
        // Order: a.txt before b.txt
        text.IndexOf("a.txt", StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("b.txt", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------
    // TEST 4 — Two blank lines appear between entries, not after the last
    // ---------------------------------------------------------------------
    [Fact]
    public void TwoBlankLinesAppearBetweenEntriesOnly()
    {
        var a = MakeContent("C:\\proj\\a.txt", "AAA");
        var b = MakeContent("C:\\proj\\b.txt", "BBB");

        var document = _composer.Compose(
            new[] { a, b },
            ComposerOptions.SingleMerged());

        var text = BundleComposer.RenderMergedText(document);

        // After AAA, we expect: \n + \n (the two blank lines).
        // The exact sequence is AAA \n \n ===== (banner for b).
        text.Should().Contain("AAA\n\n=====");
        // No trailing blank lines after the last entry.
        text.Should().NotEndWith("\n\n");
    }

    // ---------------------------------------------------------------------
    // TEST 5 — Delimiter placeholders expand correctly
    // ---------------------------------------------------------------------
    [Fact]
    public void DelimiterPlaceholdersExpand()
    {
        var content = MakeContent("C:\\proj\\file.cs", "content", index: 0);

        var options = ComposerOptions.SingleMerged() with
        {
            DelimiterTemplate = "=== [{index}] {filename} ({size}B) hash={hash} ===",
        };

        var document = _composer.Compose(new[] { content }, options);
        var text = BundleComposer.RenderMergedText(document);

        text.Should().Contain("=== [0] file.cs (7B) hash=0123");
    }

    // ---------------------------------------------------------------------
    // TEST 6 — Unreadable content renders a note, not a crash
    // ---------------------------------------------------------------------
    [Fact]
    public void UnreadableContentRendersNote()
    {
        var broken = MakeContent(
            "C:\\proj\\broken.txt",
            string.Empty,
            readError: "Circuit open");

        var document = _composer.Compose(
            new[] { broken },
            ComposerOptions.SingleMerged());

        var text = BundleComposer.RenderMergedText(document);

        text.Should().Contain("[UNREADABLE: Circuit open]");
    }

    // ---------------------------------------------------------------------
    // TEST 7 — Binary content renders a summary, not the raw bytes
    // ---------------------------------------------------------------------
    [Fact]
    public void BinaryContentRendersSummary()
    {
        var bin = MakeContent(
            "C:\\proj\\image.png",
            "\u0000PNG\u0000",
            isBinary: true);

        var document = _composer.Compose(
            new[] { bin },
            ComposerOptions.SingleMerged());

        var text = BundleComposer.RenderMergedText(document);

        text.Should().Contain("[BINARY:");
        text.Should().Contain("bytes, SHA-256=");
    }

    // ---------------------------------------------------------------------
    // TEST 8 — Empty input produces an empty document, no throw
    // ---------------------------------------------------------------------
    [Fact]
    public void EmptyInputProducesEmptyDocument()
    {
        var document = _composer.Compose(
            Array.Empty<FileContent>(),
            ComposerOptions.SingleMerged());

        document.Files.Should().BeEmpty();

        var text = BundleComposer.RenderMergedText(document);
        text.Should().BeEmpty();
    }
}
