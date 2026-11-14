// ============================================================================
// FILE:     src/FileHarvester.Tests/CodeViewer/ProjectFileReaderTests.cs
// PURPOSE:  Tests for the streaming file reader: full text with no size
//           limit, correct line counts, binary detection, and graceful
//           handling of unreadable files. Uses real temp files.
// ============================================================================

using System.Text;
using CodeViewer.Platform.FileSystem;
using FileHarvester.Core.Composition;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.Tests.CodeViewer;

public sealed class ProjectFileReaderTests : IDisposable
{
    private readonly string _root;
    private readonly ProjectFileReader _reader;

    public ProjectFileReaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cv-read-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var services = new ServiceCollection();
        services.AddFileHarvester(new ResilienceConfig(
            LogDirectory: Path.Combine(_root, "logs"),
            EnableConsoleSink: false,
            EnableFileSink: false));
        var provider = services.BuildServiceProvider();
        _reader = new ProjectFileReader(provider.GetRequiredService<IResilienceExecutor>());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string Write(string name, string content, Encoding? encoding = null)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content, encoding ?? new UTF8Encoding(false));
        return path;
    }

    [Fact]
    public async Task ReadsFullTextAndCountsLines()
    {
        var path = Write("main.py", "line1\nline2\nline3\n");

        var result = await _reader.ReadAsync(path);

        result.HasPreview.Should().BeTrue();
        result.Text.Should().Be("line1\nline2\nline3\n");
        result.LineCount.Should().Be(4);
    }

    [Fact]
    public async Task ReadsLargeFileInFullWithoutTruncation()
    {
        // ~5 MB of text: well above any arbitrary preview cap. The reader
        // must return every line.
        var builder = new StringBuilder();
        for (var i = 0; i < 100_000; i++)
        {
            builder.Append("line ").Append(i).Append('\n');
        }

        var expected = builder.ToString();
        var path = Write("big.txt", expected);

        var result = await _reader.ReadAsync(path);

        result.HasPreview.Should().BeTrue();
        result.Text.Should().Be(expected);
        result.LineCount.Should().Be(100_001);
    }

    [Fact]
    public async Task Utf8BomIsStripped()
    {
        var path = Write("bom.txt", "hello", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var result = await _reader.ReadAsync(path);

        result.HasPreview.Should().BeTrue();
        result.Text.Should().Be("hello");
    }

    [Fact]
    public async Task Utf16LeIsDecodedNotMisclassifiedAsBinary()
    {
        var path = Write("utf16.txt", "hello world", Encoding.Unicode);

        var result = await _reader.ReadAsync(path);

        result.HasPreview.Should().BeTrue();
        result.Text.Should().Be("hello world");
    }

    [Fact]
    public async Task BinaryFileReturnsNoPreviewButStaysVisible()
    {
        var path = Path.Combine(_root, "app.exe");
        var bytes = new byte[2048];
        bytes[10] = 0x00; // null byte -> binary
        new Random(42).NextBytes(bytes);
        bytes[10] = 0x00;
        File.WriteAllBytes(path, bytes);

        var result = await _reader.ReadAsync(path);

        result.HasPreview.Should().BeFalse();
        result.Reason.Should().Be(NoPreviewReason.Binary);
    }

    [Fact]
    public async Task MissingFileReturnsUnreadableNoPreview()
    {
        var result = await _reader.ReadAsync(Path.Combine(_root, "missing.txt"));

        result.HasPreview.Should().BeFalse();
        result.Reason.Should().Be(NoPreviewReason.Unreadable);
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task EmptyFileReturnsEmptyPreview()
    {
        var path = Write("empty.txt", string.Empty);

        var result = await _reader.ReadAsync(path);

        result.HasPreview.Should().BeTrue();
        result.Text.Should().BeEmpty();
        result.LineCount.Should().Be(0);
    }
}
