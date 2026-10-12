// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/CloudPathConsumerTests.cs
// PURPOSE:  Verifies that the two Core consumers of CloudPath use it
//           for cloud-shaped paths and continue to work for local paths.
//           Covers DelimiterTemplate (via BundleComposer) and
//           ZipFormatWriter. OutputOptionsViewModel (App layer) is not
//           tested here: the test project does not reference the App
//           project. See finding F-036.
// RELATION: Exercises the C.1 consumer fixes through their public APIs.
// ============================================================================

using System.IO.Compression;
using System.Text;
using FileHarvester.Core.Domain;
using FileHarvester.Core.Implementation;
using FileHarvester.Core.Writers;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud;

/// <summary>
/// Tests for the C.1 consumer fixes: cloud-shaped paths through the
/// delimiter template and the ZIP writer.
/// </summary>
public sealed class CloudPathConsumerTests
{
    private static FileContent MakeContent(string absolutePath, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var request = new FileRequest(Index: 0, RawInput: absolutePath);
        var resolved = new ResolvedFile(
            Request:         request,
            AbsolutePath:    absolutePath,
            SizeBytes:       bytes.Length,
            LastModifiedUtc: new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc),
            Confidence:      ResolutionConfidence.Exact);

        return new FileContent(
            File:        resolved,
            RawBytes:    bytes,
            DecodedText: text,
            Encoding:    new UTF8Encoding(false),
            IsBinary:    false,
            Sha256:      "0000000000000000000000000000000000000000000000000000000000000000");
    }

    [Fact]
    public void DelimiterTemplateExpandsCloudPathFilename()
    {
        var content = MakeContent("s3://bucket/docs/a.txt", "body");

        var options = ComposerOptions.SingleMerged() with
        {
            DelimiterTemplate = "=== {filename} ===",
        };

        var composer = new BundleComposer();
        var document = composer.Compose(new[] { content }, options);
        var text = BundleComposer.RenderMergedText(document);

        text.Should().Contain("=== a.txt ===");
        text.Should().NotContain("docs/a.txt ===");
    }

    [Fact]
    public void DelimiterTemplateExpandsLocalPathFilename()
    {
        var content = MakeContent(@"C:\proj\a.txt", "body");

        var options = ComposerOptions.SingleMerged() with
        {
            DelimiterTemplate = "=== {filename} ===",
        };

        var composer = new BundleComposer();
        var document = composer.Compose(new[] { content }, options);
        var text = BundleComposer.RenderMergedText(document);

        text.Should().Contain("=== a.txt ===");
    }

    [Fact]
    public async Task ZipWriterUsesCloudFilenameInArchiveEntry()
    {
        var content = MakeContent("s3://bucket/docs/a.txt", "cloud body");

        var document = new ComposedDocument(
            Files: new[] { content },
            Mode: OutputMode.SingleMerged,
            DelimiterTemplate: string.Empty,
            TwoBlankLines: false,
            IncludeTableOfContents: false,
            IncludeManifest: false,
            PreserveFolderStructure: false,
            IncludeHeaders: false,
            IncludeCodeFences: false);

        var writer = new ZipFormatWriter();
        using var stream = new MemoryStream();
        await writer.WriteAsync(document, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        archive.Entries.Should().HaveCount(1);
        archive.Entries[0].FullName.Should().Be("001-a.txt");
    }
}
