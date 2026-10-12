// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Services/CloudOutputSinkTests.cs
// PURPOSE:  Unit tests for CloudOutputSink. Uses StubCloudProvider and a
//           local StubOverwriteConfirmer. Verifies the try/finally
//           lifecycle, byte counting, overwrite confirmation, the
//           apply-to-all scope, and cancellation.
// RELATION: Consumes CloudOutputSink, StubCloudProvider, and a local
//           StubOverwriteConfirmer (the OutputSinkTests version is
//           private to that file). No I/O, no network.
// ============================================================================

using System.Text;
using FileHarvester.Core.Domain;
using FileHarvester.Core.Writers;
using FileHarvester.Platform.Cloud.Services;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud.Services;

/// <summary>
/// Tests for <see cref="CloudOutputSink"/>.
/// </summary>
public sealed class CloudOutputSinkTests
{
    // ------------------------------------------------------------------
    // Local test doubles
    // ------------------------------------------------------------------

    private sealed class StubOverwriteConfirmer : IOverwriteConfirmer
    {
        private readonly Queue<OverwritePromptResult> _results = new();
        public List<string> PromptedPaths { get; } = new();

        public void Enqueue(OverwriteDecision decision, bool applyToAll)
        {
            _results.Enqueue(new OverwritePromptResult(decision, applyToAll));
        }

        public Task<OverwritePromptResult> ConfirmAsync(
            string targetPath,
            CancellationToken cancellationToken = default)
        {
            PromptedPaths.Add(targetPath);
            if (_results.Count == 0)
            {
                return Task.FromResult(new OverwritePromptResult(
                    OverwriteDecision.Cancel, false));
            }
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class StubFormatWriter : IFormatWriter
    {
        public string Extension => ".stub";
        public string DisplayName => "Stub";
        public string MimeType => "application/octet-stream";
        public bool SupportsStreaming => true;

        public async Task WriteAsync(
            ComposedDocument document,
            Stream output,
            CancellationToken cancellationToken = default)
        {
            foreach (var fc in document.Files)
            {
                await output.WriteAsync(fc.RawBytes, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static FileContent MakeContent(string absolutePath, string text, int index = 0)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var request = new FileRequest(Index: index, RawInput: absolutePath);
        var resolved = new ResolvedFile(
            Request: request,
            AbsolutePath: absolutePath,
            SizeBytes: bytes.Length,
            LastModifiedUtc: DateTime.UtcNow,
            Confidence: ResolutionConfidence.Searched);

        return new FileContent(
            File: resolved,
            RawBytes: bytes,
            DecodedText: text,
            Encoding: new UTF8Encoding(false),
            IsBinary: false,
            Sha256: "0");
    }

    private static ComposedDocument MakeMergedDoc(string path, string text) => new(
        Files: new[] { MakeContent(path, text) },
        Mode: OutputMode.SingleMerged,
        DelimiterTemplate: string.Empty,
        TwoBlankLines: false,
        IncludeTableOfContents: false,
        IncludeManifest: false);

    private static ComposedDocument MakeSeparateDoc(params (string Path, string Text)[] files)
    {
        var contents = files
            .Select((f, i) => MakeContent(f.Path, f.Text, i))
            .ToArray();

        return new ComposedDocument(
            Files: contents,
            Mode: OutputMode.SeparateFiles,
            DelimiterTemplate: string.Empty,
            TwoBlankLines: false,
            IncludeTableOfContents: false,
            IncludeManifest: false);
    }

    private static CloudOutputSink Build(
        StubCloudProvider provider,
        StubOverwriteConfirmer confirmer,
        PathSchemeKind kind = PathSchemeKind.S3)
    {
        var registry = new FormatWriterRegistry(new IFormatWriter[] { new StubFormatWriter() });
        return new CloudOutputSink((k, _, _) => Task.FromResult(k == kind ? CloudProviderResolution.Resolved(provider) : CloudProviderResolution.Failed(CloudResolutionFailure.NoProviderRegistered)), registry, confirmer);
    }

    private static OutputSinkOptions Options(string folder, string? fileName = null) => new(
        DestinationFolder: folder,
        Extension: ".stub",
        FileName: fileName);

    // ------------------------------------------------------------------
    // Tests
    // ------------------------------------------------------------------

    [Fact]
    public async Task SaveMergedOpensWriteHandleAndCommits()
    {
        var provider = new StubCloudProvider();
        provider.OnExists = _ => Task.FromResult(false);
        var committed = false;
        provider.OnOpenWrite = (path, ct) =>
        {
            var ms = new MemoryStream();
            Func<CancellationToken, Task> commit = _ =>
            {
                committed = true;
                return Task.CompletedTask;
            };
            return Task.FromResult(new CloudWriteHandle(ms, commit));
        };

        var confirmer = new StubOverwriteConfirmer();
        var sink = Build(provider, confirmer);

        var doc = MakeMergedDoc("s3://bucket/a.txt", "hello");
        var result = await sink.SaveAsync(doc, Options("s3://bucket/out", "bundle"));

        result.Success.Should().BeTrue();
        committed.Should().BeTrue();
        confirmer.PromptedPaths.Should().BeEmpty();
        provider.Calls.Should().Contain(c => c.StartsWith("OpenWriteAsync:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaveMergedUsesCountingStream()
    {
        var provider = new StubCloudProvider();
        provider.OnExists = _ => Task.FromResult(false);
        provider.OnOpenWrite = (_, _) => Task.FromResult(new CloudWriteHandle(
            new MemoryStream(), _ => Task.CompletedTask));

        var confirmer = new StubOverwriteConfirmer();
        var sink = Build(provider, confirmer);

        var doc = MakeMergedDoc("s3://bucket/a.txt", "hello cloud");
        var result = await sink.SaveAsync(doc, Options("s3://bucket/out", "bundle"));

        result.Success.Should().BeTrue();
        result.BytesWritten.Should().Be(11);
    }

    [Fact]
    public async Task SaveSeparateOpensOneHandlePerFile()
    {
        var provider = new StubCloudProvider();
        provider.OnExists = _ => Task.FromResult(false);
        provider.OnOpenWrite = (_, _) => Task.FromResult(new CloudWriteHandle(
            new MemoryStream(), _ => Task.CompletedTask));

        var confirmer = new StubOverwriteConfirmer();
        var sink = Build(provider, confirmer);

        var doc = MakeSeparateDoc(("s3://bucket/a.txt", "A"), ("s3://bucket/b.txt", "B"));
        var result = await sink.SaveAsync(doc, Options("s3://bucket/out"));

        result.Success.Should().BeTrue();
        result.FileCount.Should().Be(2);

        var openWriteCalls = provider.Calls.Count(c => c.StartsWith("OpenWriteAsync:", StringComparison.Ordinal));
        openWriteCalls.Should().Be(2);
    }

    [Fact]
    public async Task SaveMergedCollisionCancelReturnsFail()
    {
        var provider = new StubCloudProvider();
        provider.OnExists = _ => Task.FromResult(true);

        var confirmer = new StubOverwriteConfirmer();
        confirmer.Enqueue(OverwriteDecision.Cancel, applyToAll: false);
        var sink = Build(provider, confirmer);

        var doc = MakeMergedDoc("s3://bucket/a.txt", "hello");
        var result = await sink.SaveAsync(doc, Options("s3://bucket/out", "bundle"));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Cancelled by user");
        confirmer.PromptedPaths.Should().HaveCount(1);
    }

    [Fact]
    public async Task SaveMergedCollisionReplaceProceeds()
    {
        var provider = new StubCloudProvider();
        provider.OnExists = _ => Task.FromResult(true);
        provider.OnOpenWrite = (_, _) => Task.FromResult(new CloudWriteHandle(
            new MemoryStream(), _ => Task.CompletedTask));

        var confirmer = new StubOverwriteConfirmer();
        confirmer.Enqueue(OverwriteDecision.Replace, applyToAll: false);
        var sink = Build(provider, confirmer);

        var doc = MakeMergedDoc("s3://bucket/a.txt", "hello");
        var result = await sink.SaveAsync(doc, Options("s3://bucket/out", "bundle"));

        result.Success.Should().BeTrue();
        confirmer.PromptedPaths.Should().HaveCount(1);
    }

    [Fact]
    public async Task SaveSeparateCollisionApplyToAllRemainingPromptsOnce()
    {
        var provider = new StubCloudProvider();
        // Every target exists, so every file would collide.
        provider.OnExists = _ => Task.FromResult(true);
        provider.OnOpenWrite = (_, _) => Task.FromResult(new CloudWriteHandle(
            new MemoryStream(), _ => Task.CompletedTask));

        var confirmer = new StubOverwriteConfirmer();
        confirmer.Enqueue(OverwriteDecision.Replace, applyToAll: true);
        var sink = Build(provider, confirmer);

        var doc = MakeSeparateDoc(
            ("s3://bucket/a.txt", "A"),
            ("s3://bucket/b.txt", "B"),
            ("s3://bucket/c.txt", "C"));

        var result = await sink.SaveAsync(doc, Options("s3://bucket/out"));

        result.Success.Should().BeTrue();
        confirmer.PromptedPaths.Should().HaveCount(1);
    }

    [Fact]
    public async Task SaveMergedDestinationIsUriShaped()
    {
        var provider = new StubCloudProvider();
        provider.OnExists = _ => Task.FromResult(false);
        provider.OnOpenWrite = (_, _) => Task.FromResult(new CloudWriteHandle(
            new MemoryStream(), _ => Task.CompletedTask));

        var confirmer = new StubOverwriteConfirmer();
        var sink = Build(provider, confirmer);

        var doc = MakeMergedDoc("s3://bucket/a.txt", "hello");
        var result = await sink.SaveAsync(doc, Options("s3://bucket/out", "bundle"));

        result.Success.Should().BeTrue();
        result.OutputPath.Should().StartWith("s3://");
    }

    [Fact]
    public async Task SaveMergedCancellationPropagates()
    {
        var provider = new StubCloudProvider();
        provider.OnExists = _ => Task.FromResult(false);
        provider.OnOpenWrite = (_, _) => throw new OperationCanceledException();

        var confirmer = new StubOverwriteConfirmer();
        var sink = Build(provider, confirmer);

        var doc = MakeMergedDoc("s3://bucket/a.txt", "hello");

        var act = async () => await sink.SaveAsync(
            doc, Options("s3://bucket/out", "bundle"));
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
