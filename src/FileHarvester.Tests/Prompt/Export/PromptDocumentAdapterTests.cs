// ============================================================================
// FILE:     src/FileHarvester.Tests/Prompt/Export/PromptDocumentAdapterTests.cs
// PURPOSE:  Guards PromptDocumentAdapter.ToComposedDocument: the SingleMerged
//           shape with one file; the writer's extension in the synthetic
//           AbsolutePath; the SHA-256 matches the body's UTF-8 bytes; the
//           code-fences heuristic (Markdown body + non-.md writer -> true;
//           Markdown -> .md -> false; PlainText -> false).
// RELATION: Added at P3.1. Consumes PromptDocumentAdapter
//           (Prompt.Platform.Export) and a FakeFormatWriter for IFormatWriter.
//           No DB, no UI, no file I/O.
// NOTES:    - The FakeFormatWriter is a minimal IFormatWriter with the given
//             extension; WriteAsync is a no-op (the adapter's output is not
//             written here - the writer's behavior is P3.2's test scope).
//           - The prompt's record shape is (Id, Title, Body, Format,
//             CategoryId, SubCategoryId, Description, IsFavorite, IsDeleted,
//             Tags, CreatedAt, UpdatedAt). Verified by the CS7036 error from
//             the P2.7.x build of PromptListViewModelTests.
// ============================================================================

using FileHarvester.Core.Domain;
using FileHarvester.Core.Writers;
using FluentAssertions;
using Prompt.Core.Domain;
using Prompt.Platform.Export;
using Xunit;
using PromptRecord = Prompt.Core.Domain.Prompt;

namespace FileHarvester.Tests.Prompt.Export;

/// <summary>
/// Guards PromptDocumentAdapter.ToComposedDocument. Four facts: the
/// SingleMerged shape, the writer's extension, the SHA-256, the code-fences
/// heuristic.
/// </summary>
public sealed class PromptDocumentAdapterTests
{
    private static PromptRecord MakePrompt(string title, string body, BodyFormat format = BodyFormat.PlainText) =>
        new(Guid.NewGuid(), title, body, format, null, null, null, false, false,
            Array.Empty<string>(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class FakeFormatWriter : IFormatWriter
    {
        public FakeFormatWriter(string extension) { Extension = extension; }
        public string Extension { get; }
        public string DisplayName => "Fake " + Extension;
        public string MimeType => "text/plain";
        public bool SupportsStreaming => false;
        public Task WriteAsync(ComposedDocument document, Stream output, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Proves the ComposedDocument is SingleMerged with one FileContent, the
    /// default delimiter, and no per-file headers.
    /// </summary>
    [Fact]
    public void ToComposedDocumentBuildsSingleMergedWithOneFile()
    {
        // ARRANGE
        var prompt = MakePrompt("My Prompt", "Body content.");
        var writer = new FakeFormatWriter(".txt");

        // ACT
        var doc = PromptDocumentAdapter.ToComposedDocument(prompt, writer);

        // ASSERT
        doc.Files.Should().HaveCount(1);
        doc.Mode.Should().Be(OutputMode.SingleMerged);
        doc.DelimiterTemplate.Should().Be(ComposedDocument.DefaultDelimiter);
        doc.IncludeHeaders.Should().BeFalse();
    }

    /// <summary>
    /// Proves the FileContent's AbsolutePath is the sanitized title + the
    /// writer's extension.
    /// </summary>
    [Fact]
    public void ToComposedDocumentUsesWriterExtension()
    {
        // ARRANGE
        var prompt = MakePrompt("My Prompt", "Body.");
        var writer = new FakeFormatWriter(".md");

        // ACT
        var doc = PromptDocumentAdapter.ToComposedDocument(prompt, writer);

        // ASSERT
        doc.Files[0].File.AbsolutePath.Should().Be("My Prompt.md");
    }

    /// <summary>
    /// Proves the FileContent's SHA-256 matches the body's UTF-8 bytes,
    /// lowercase hex.
    /// </summary>
    [Fact]
    public void ToComposedDocumentSha256MatchesBody()
    {
        // ARRANGE
        var prompt = MakePrompt("P", "Body text.");
        var writer = new FakeFormatWriter(".txt");
        var expected = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("Body text."))).ToLowerInvariant();

        // ACT
        var doc = PromptDocumentAdapter.ToComposedDocument(prompt, writer);

        // ASSERT
        doc.Files[0].Sha256.Should().Be(expected);
    }

    /// <summary>
    /// Proves the code-fences heuristic: Markdown to .txt -> true; Markdown
    /// to .md -> false; PlainText to .txt -> false.
    /// </summary>
    [Fact]
    public void ToComposedDocumentMarkdownToNonMarkdownEnablesCodeFences()
    {
        // ARRANGE
        var markdownPrompt = MakePrompt("M", "# Heading", BodyFormat.Markdown);
        var plainPrompt = MakePrompt("P", "plain", BodyFormat.PlainText);
        var txtWriter = new FakeFormatWriter(".txt");
        var mdWriter = new FakeFormatWriter(".md");

        // ACT
        var mdToTxt = PromptDocumentAdapter.ToComposedDocument(markdownPrompt, txtWriter);
        var mdToMd = PromptDocumentAdapter.ToComposedDocument(markdownPrompt, mdWriter);
        var plainToTxt = PromptDocumentAdapter.ToComposedDocument(plainPrompt, txtWriter);

        // ASSERT
        mdToTxt.IncludeCodeFences.Should().BeTrue();
        mdToMd.IncludeCodeFences.Should().BeFalse();
        plainToTxt.IncludeCodeFences.Should().BeFalse();
    }
}