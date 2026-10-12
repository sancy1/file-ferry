// ============================================================================
// FILE:     src/FileHarvester.Tests/OutputSinkTests.cs
// PURPOSE:  Unit tests for OutputSink's overwrite-confirmation guards.
//           Constructs the sink directly so a stub confirmer can be
//           injected; uses a real IResilienceExecutor, real
//           FormatWriterRegistry, real DefaultPathsProvider, and a
//           deterministic stub IFormatWriter.
// RELATION: Exercises OutputSink (Phase D.1.2). Does not use
//           IClassFixture<TestHarness>; the tests create their own
//           provider and temp directory per test.
// ============================================================================

using System.Text;
using FileHarvester.Core.Composition;
using FileHarvester.Core.Domain;
using FileHarvester.Core.Implementation;
using FileHarvester.Core.Platform;
using FileHarvester.Core.Writers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Extensions;
using Portfolio.Resilience.Sinks;
using Xunit;

namespace FileHarvester.Tests;

/// <summary>
/// Unit tests for the overwrite-confirmation guards in
/// <see cref="OutputSink"/>.
/// </summary>
public sealed class OutputSinkTests : IDisposable
{
    private readonly string _tempRoot;

    public OutputSinkTests()
    {
        _tempRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "file-ferry-outputsink-tests",
            Guid.NewGuid().ToString("N")));

        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; matches TestHarness.Dispose.
        }
    }

    // ------------------------------------------------------------------
    // Provider construction
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds a ServiceProvider with both policies the sink calls by
    /// name: "file-read" (for the existence check) and "bundle-write"
    /// (for the write itself). Policy names come from PolicyNames so
    /// they cannot drift from the production constants.
    /// </summary>
    /// <param name="fileReadFailureThreshold">
    /// Consecutive transient failures that open the "file-read"
    /// circuit. Default 3.
    /// </param>
    /// <param name="bundleWriteFailureThreshold">
    /// Consecutive transient failures that open the "bundle-write"
    /// circuit. Default 5.
    /// </param>
    /// <returns>A provider. The caller must dispose it.</returns>
    private static ServiceProvider BuildProviderWithPolicies(
        int fileReadFailureThreshold = 3,
        int bundleWriteFailureThreshold = 5)
    {
        var services = new ServiceCollection();

        services.AddPortfolioResilience(r =>
        {
            r.AddLogSink(new ConsoleLogSink());

            r.AddPolicy(PolicyNames.FileRead, p =>
            {
                p.Retry.MaxAttempts           = 2;
                p.Retry.BaseDelayMs           = 10;
                p.Retry.JitterRatio           = 0.0;

                p.Circuit.FailureThreshold    = fileReadFailureThreshold;
                p.Circuit.OpenDurationSeconds = 30;
                p.Circuit.SuccessThreshold    = 1;
                p.Circuit.OnlyCountTransient  = true;

                p.Timeout.TimeoutMs           = 5_000;
            });

            r.AddPolicy(PolicyNames.BundleWrite, p =>
            {
                p.Retry.MaxAttempts           = 1;
                p.Retry.BaseDelayMs           = 10;
                p.Retry.JitterRatio           = 0.0;

                p.Circuit.FailureThreshold    = bundleWriteFailureThreshold;
                p.Circuit.OpenDurationSeconds = 30;
                p.Circuit.SuccessThreshold    = 1;
                p.Circuit.OnlyCountTransient  = true;

                p.Timeout.TimeoutMs           = 30_000;
            });

            r.ErrorClassification.TransientExceptionTypeNames.Add(
                "System.IO.IOException");
        });

        return services.BuildServiceProvider();
    }

    // ------------------------------------------------------------------
    // Test doubles
    // ------------------------------------------------------------------

    /// <summary>
    /// An IOverwriteConfirmer that returns scripted results and records
    /// every prompt. Single-threaded; the sink calls it from one thread
    /// at a time within a single SaveAsync invocation.
    /// </summary>
    private sealed class StubOverwriteConfirmer : IOverwriteConfirmer
    {
        private readonly Queue<OverwritePromptResult> _results = new();
        private readonly List<string> _promptedPaths = new();

        public Func<string, CancellationToken, Task<OverwritePromptResult>>? OnConfirm { get; set; }

        public void Enqueue(OverwriteDecision decision, bool applyToAll)
        {
            _results.Enqueue(new OverwritePromptResult(decision, applyToAll));
        }

        public int PromptCount => _promptedPaths.Count;

        public IReadOnlyList<string> PromptedPaths => _promptedPaths;

        public Task<OverwritePromptResult> ConfirmAsync(
            string targetPath,
            CancellationToken cancellationToken = default)
        {
            _promptedPaths.Add(targetPath);

            if (OnConfirm is not null)
            {
                return OnConfirm(targetPath, cancellationToken);
            }

            if (_results.Count == 0)
            {
                // Fail closed if the test forgot to script a response.
                return Task.FromResult(new OverwritePromptResult(
                    OverwriteDecision.Cancel, false));
            }

            return Task.FromResult(_results.Dequeue());
        }
    }

    /// <summary>
    /// A deterministic IFormatWriter. Writes FileContent.RawBytes for
    /// each readable file, and a fixed marker for an unreadable one.
    /// Writes only to the supplied stream; opens no file.
    /// </summary>
    private sealed class StubFormatWriter : IFormatWriter
    {
        private static readonly byte[] UnreadableMarker =
            Encoding.UTF8.GetBytes("[UNREADABLE]");

        public string Extension => ".stub";
        public string DisplayName => "Stub";
        public string MimeType => "application/octet-stream";
        public bool SupportsStreaming => true;

        public async Task WriteAsync(
            ComposedDocument document,
            Stream output,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(output);

            foreach (var fc in document.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var bytes = fc.IsUnreadable ? UnreadableMarker : fc.RawBytes;
                await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // ------------------------------------------------------------------
    // Sink construction
    // ------------------------------------------------------------------

    /// <summary>
    /// Constructs an OutputSink with the test-supplied confirmer and a
    /// real resilience executor from a fresh provider. The writer
    /// registry contains exactly one writer: the stub.
    /// </summary>
    private static OutputSink BuildSink(
        IResilienceExecutor executor,
        IOverwriteConfirmer confirmer)
    {
        var registry = new FormatWriterRegistry(new IFormatWriter[]
        {
            new StubFormatWriter(),
        });

        var paths = new DefaultPathsProvider();

        return new OutputSink(registry, paths, executor, confirmer);
    }


    // ------------------------------------------------------------------
    // TEST 1 - Merged, no collision: file written, confirmer not called
    // ------------------------------------------------------------------

    [Fact]
    public async Task MergedNoCollisionWritesFileAndDoesNotPrompt()
    {
        using var provider = BuildProviderWithPolicies();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var confirmer = new StubOverwriteConfirmer();
        var sink = BuildSink(executor, confirmer);

        var sourcePath = Path.Combine(_tempRoot, "source.txt");
        var sourceBytes = Encoding.UTF8.GetBytes("hello world");
        File.WriteAllBytes(sourcePath, sourceBytes);

        var document = MakeSingleMergedDocument(sourcePath, sourceBytes);
        var options = new OutputSinkOptions(
            DestinationFolder: _tempRoot,
            Extension: ".stub",
            FileName: "out-no-collision");

        var result = await sink.SaveAsync(document, options);

        result.Success.Should().BeTrue();
        confirmer.PromptCount.Should().Be(0, "there is no collision");
        var outPath = Path.Combine(_tempRoot, "out-no-collision.stub");
        File.Exists(outPath).Should().BeTrue();
        File.ReadAllBytes(outPath).Should().Equal(sourceBytes);
    }

    // ------------------------------------------------------------------
    // TEST 2 - Merged, collision + Cancel: file untouched, Fail returned
    // ------------------------------------------------------------------

    [Fact]
    public async Task MergedCollisionCancelLeavesFileUntouchedAndReturnsFail()
    {
        using var provider = BuildProviderWithPolicies();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var confirmer = new StubOverwriteConfirmer();
        confirmer.Enqueue(OverwriteDecision.Cancel, applyToAll: false);
        var sink = BuildSink(executor, confirmer);

        var sourcePath = Path.Combine(_tempRoot, "source2.txt");
        var sourceBytes = Encoding.UTF8.GetBytes("new content");
        File.WriteAllBytes(sourcePath, sourceBytes);

        var outPath = Path.Combine(_tempRoot, "out-cancel.stub");
        var existingBytes = Encoding.UTF8.GetBytes("existing content");
        File.WriteAllBytes(outPath, existingBytes);

        var document = MakeSingleMergedDocument(sourcePath, sourceBytes);
        var options = new OutputSinkOptions(
            DestinationFolder: _tempRoot,
            Extension: ".stub",
            FileName: "out-cancel");

        var result = await sink.SaveAsync(document, options);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Cancelled by user: file exists");
        confirmer.PromptCount.Should().Be(1);
        File.ReadAllBytes(outPath).Should().Equal(existingBytes,
            "the existing file must be untouched after Cancel");
    }

    // ------------------------------------------------------------------
    // TEST 3 - Merged, collision + Replace: file overwritten, Ok returned
    // ------------------------------------------------------------------

    [Fact]
    public async Task MergedCollisionReplaceOverwritesFileAndReturnsOk()
    {
        using var provider = BuildProviderWithPolicies();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var confirmer = new StubOverwriteConfirmer();
        confirmer.Enqueue(OverwriteDecision.Replace, applyToAll: false);
        var sink = BuildSink(executor, confirmer);

        var sourcePath = Path.Combine(_tempRoot, "source3.txt");
        var sourceBytes = Encoding.UTF8.GetBytes("replacement content");
        File.WriteAllBytes(sourcePath, sourceBytes);

        var outPath = Path.Combine(_tempRoot, "out-replace.stub");
        var existingBytes = Encoding.UTF8.GetBytes("old content");
        File.WriteAllBytes(outPath, existingBytes);

        var document = MakeSingleMergedDocument(sourcePath, sourceBytes);
        var options = new OutputSinkOptions(
            DestinationFolder: _tempRoot,
            Extension: ".stub",
            FileName: "out-replace");

        var result = await sink.SaveAsync(document, options);

        result.Success.Should().BeTrue();
        confirmer.PromptCount.Should().Be(1);
        File.ReadAllBytes(outPath).Should().Equal(sourceBytes,
            "Replace must overwrite the existing file with the new bytes");
    }

    // ------------------------------------------------------------------
    // TEST 4 - Separate, three collisions, Replace+ApplyToAll prompts once
    // ------------------------------------------------------------------

    [Fact]
    public async Task SeparateMultipleCollisionsReplaceApplyToAllPromptsOnceAndOverwritesAll()
    {
        using var provider = BuildProviderWithPolicies();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var confirmer = new StubOverwriteConfirmer();
        confirmer.Enqueue(OverwriteDecision.Replace, applyToAll: true);
        var sink = BuildSink(executor, confirmer);

        var files = new List<FileContent>();
        for (var i = 0; i < 3; i++)
        {
            var name = "sep-c-" + i;
            var src = Path.Combine(_tempRoot, name + ".txt");
            var bytes = Encoding.UTF8.GetBytes("content-" + i);
            File.WriteAllBytes(src, bytes);

            var outPath = Path.Combine(_tempRoot, name + ".stub");
            File.WriteAllBytes(outPath, Encoding.UTF8.GetBytes("old-" + i));

            files.Add(MakeFileContent(src, bytes, i));
        }

        var document = MakeSeparateDocument(files);
        var options = new OutputSinkOptions(
            DestinationFolder: _tempRoot,
            Extension: ".stub",
            FileName: null);

        var result = await sink.SaveAsync(document, options);

        result.Success.Should().BeTrue();
        confirmer.PromptCount.Should().Be(1,
            "ApplyToAllRemaining must suppress the second and third prompts");

        for (var i = 0; i < 3; i++)
        {
            var outPath = Path.Combine(_tempRoot, "sep-c-" + i + ".stub");
            File.ReadAllBytes(outPath).Should().Equal(
                Encoding.UTF8.GetBytes("content-" + i));
        }
    }

    // ------------------------------------------------------------------
    // TEST 5 - Separate, collision on file 3 of 5, Cancel aborts
    // ------------------------------------------------------------------

    [Fact]
    public async Task SeparateCollisionOnFile3CancelLeavesFiles1And2WrittenAndReturnsFail()
    {
        using var provider = BuildProviderWithPolicies();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var confirmer = new StubOverwriteConfirmer();
        confirmer.Enqueue(OverwriteDecision.Cancel, applyToAll: false);
        var sink = BuildSink(executor, confirmer);

        var files = new List<FileContent>();
        for (var i = 0; i < 5; i++)
        {
            var name = "sep-abort-" + i;
            var src = Path.Combine(_tempRoot, name + ".txt");
            var bytes = Encoding.UTF8.GetBytes("c-" + i);
            File.WriteAllBytes(src, bytes);
            files.Add(MakeFileContent(src, bytes, i));

            // Files 0, 1: no pre-existing output.
            // File 2: pre-existing output (collision).
            // Files 3, 4: no pre-existing output (but must NOT be written).
            if (i == 2)
            {
                var outPath = Path.Combine(_tempRoot, name + ".stub");
                File.WriteAllBytes(outPath, Encoding.UTF8.GetBytes("existing-2"));
            }
        }

        var document = MakeSeparateDocument(files);
        var options = new OutputSinkOptions(
            DestinationFolder: _tempRoot,
            Extension: ".stub",
            FileName: null);

        var result = await sink.SaveAsync(document, options);

        result.Success.Should().BeFalse();
        confirmer.PromptCount.Should().Be(1);

        File.Exists(Path.Combine(_tempRoot, "sep-abort-0.stub")).Should().BeTrue();
        File.Exists(Path.Combine(_tempRoot, "sep-abort-1.stub")).Should().BeTrue();

        File.ReadAllBytes(Path.Combine(_tempRoot, "sep-abort-2.stub"))
            .Should().Equal(Encoding.UTF8.GetBytes("existing-2"),
                "file 3 must not have been overwritten after Cancel");

        File.Exists(Path.Combine(_tempRoot, "sep-abort-3.stub")).Should().BeFalse(
            "file 4 must not be written after Cancel");
        File.Exists(Path.Combine(_tempRoot, "sep-abort-4.stub")).Should().BeFalse(
            "file 5 must not be written after Cancel");
    }

    // ------------------------------------------------------------------
    // TEST 6 - Separate, Cancel + ApplyToAll aborts without further writes
    // ------------------------------------------------------------------

    [Fact]
    public async Task SeparateCollisionCancelApplyToAllAbortsWithoutFurtherPromptsOrWrites()
    {
        using var provider = BuildProviderWithPolicies();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var confirmer = new StubOverwriteConfirmer();
        confirmer.Enqueue(OverwriteDecision.Cancel, applyToAll: true);
        var sink = BuildSink(executor, confirmer);

        var files = new List<FileContent>();
        for (var i = 0; i < 3; i++)
        {
            var name = "sep-cancelall-" + i;
            var src = Path.Combine(_tempRoot, name + ".txt");
            var bytes = Encoding.UTF8.GetBytes("cc-" + i);
            File.WriteAllBytes(src, bytes);
            files.Add(MakeFileContent(src, bytes, i));

            if (i == 1)
            {
                var outPath = Path.Combine(_tempRoot, name + ".stub");
                File.WriteAllBytes(outPath, Encoding.UTF8.GetBytes("existing-1"));
            }
        }

        var document = MakeSeparateDocument(files);
        var options = new OutputSinkOptions(
            DestinationFolder: _tempRoot,
            Extension: ".stub",
            FileName: null);

        var result = await sink.SaveAsync(document, options);

        result.Success.Should().BeFalse();
        confirmer.PromptCount.Should().Be(1);

        File.Exists(Path.Combine(_tempRoot, "sep-cancelall-0.stub")).Should().BeTrue();

        File.ReadAllBytes(Path.Combine(_tempRoot, "sep-cancelall-1.stub"))
            .Should().Equal(Encoding.UTF8.GetBytes("existing-1"));

        File.Exists(Path.Combine(_tempRoot, "sep-cancelall-2.stub")).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // TEST 7 - file-read circuit open: existence check fails closed
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExistenceCheckUsesFileReadPolicyAndFailsClosedWhenCircuitOpen()
    {
        using var provider = BuildProviderWithPolicies(
            fileReadFailureThreshold: 3);
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var monitor = provider.GetRequiredService<ICircuitBreakerMonitor>();

        // Pre-trip the "file-read" circuit. The policy has MaxAttempts = 2,
        // meaning 1 initial + 2 retries = 3 attempts per call, all failing
        // because the operation always throws. FailureThreshold = 3, so a
        // single call already exceeds it; three calls are more than enough
        // and are used for a deterministic margin.
        for (var i = 0; i < 3; i++)
        {
            try
            {
                await executor.ExecuteAsync(
                    policyName: PolicyNames.FileRead,
                    operation: _ => throw new IOException("pre-trip"),
                    ct: CancellationToken.None);
            }
            catch (Portfolio.Resilience.Errors.ResilienceException) { }
        }

        var snapshot = monitor.Snapshot()
            .FirstOrDefault(c => c.PolicyName == PolicyNames.FileRead);
        snapshot.Should().NotBeNull();
        snapshot!.State.Should().Be(CircuitState.Open,
            "the pre-trip must have opened the file-read circuit");

        var confirmer = new StubOverwriteConfirmer();
        confirmer.Enqueue(OverwriteDecision.Cancel, applyToAll: false);
        var sink = BuildSink(executor, confirmer);

        var sourcePath = Path.Combine(_tempRoot, "src-7.txt");
        var sourceBytes = Encoding.UTF8.GetBytes("circuit-test");
        File.WriteAllBytes(sourcePath, sourceBytes);

        // No output file is created. If the existence check ran normally,
        // File.Exists would return false and the confirmer would NOT be
        // called. With the circuit open, the fallback returns true, and
        // the confirmer IS called. That is the fail-closed assertion.
        var document = MakeSingleMergedDocument(sourcePath, sourceBytes);
        var options = new OutputSinkOptions(
            DestinationFolder: _tempRoot,
            Extension: ".stub",
            FileName: "out-circuit");

        var result = await sink.SaveAsync(document, options);

        result.Success.Should().BeFalse();
        confirmer.PromptCount.Should().Be(1,
            "the circuit-open fallback must treat the target as existing");
    }

    // ------------------------------------------------------------------
    // TEST 8 - Caller cancellation while the prompt is open
    // ------------------------------------------------------------------

    [Fact]
    public async Task CallerCancellationWhilePromptOpenReturnsCancel()
    {
        using var provider = BuildProviderWithPolicies();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var confirmer = new StubOverwriteConfirmer();

        using var cts = new CancellationTokenSource();
        var entered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        confirmer.OnConfirm = async (path, ct) =>
        {
            entered.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                // Expected: caller cancelled while the prompt was open.
            }

            return new OverwritePromptResult(OverwriteDecision.Cancel, false);
        };

        var sink = BuildSink(executor, confirmer);

        var sourcePath = Path.Combine(_tempRoot, "src-8.txt");
        var sourceBytes = Encoding.UTF8.GetBytes("cancel-test");
        File.WriteAllBytes(sourcePath, sourceBytes);

        var outPath = Path.Combine(_tempRoot, "out-cancel-test.stub");
        File.WriteAllBytes(outPath, Encoding.UTF8.GetBytes("existing"));

        var document = MakeSingleMergedDocument(sourcePath, sourceBytes);
        var options = new OutputSinkOptions(
            DestinationFolder: _tempRoot,
            Extension: ".stub",
            FileName: "out-cancel-test");

        var saveTask = sink.SaveAsync(document, options, cancellationToken: cts.Token);

        await entered.Task;
        cts.Cancel();

        var result = await saveTask;

        result.Success.Should().BeFalse();
        confirmer.PromptCount.Should().Be(1);
    }

    // ------------------------------------------------------------------
    // Test data helpers
    // ------------------------------------------------------------------

    private static FileContent MakeFileContent(string path, byte[] bytes, int index)
    {
        var request = new FileRequest(Index: index, RawInput: path);
        var resolved = new ResolvedFile(
            Request:         request,
            AbsolutePath:    path,
            SizeBytes:       bytes.Length,
            LastModifiedUtc: DateTime.UtcNow,
            Confidence:      ResolutionConfidence.Exact);

        return new FileContent(
            File:        resolved,
            RawBytes:    bytes,
            DecodedText: Encoding.UTF8.GetString(bytes),
            Encoding:    new UTF8Encoding(false),
            IsBinary:    false,
            Sha256:      "0000000000000000000000000000000000000000000000000000000000000000");
    }

    private static ComposedDocument MakeSingleMergedDocument(string path, byte[] bytes)
    {
        var fc = MakeFileContent(path, bytes, 0);
        return new ComposedDocument(
            Files: new[] { fc },
            Mode: OutputMode.SingleMerged,
            DelimiterTemplate: string.Empty,
            TwoBlankLines: false,
            IncludeTableOfContents: false,
            IncludeManifest: false,
            PreserveFolderStructure: false,
            IncludeHeaders: false,
            IncludeCodeFences: false);
    }

    private static ComposedDocument MakeSeparateDocument(IReadOnlyList<FileContent> files)
    {
        return new ComposedDocument(
            Files: files,
            Mode: OutputMode.SeparateFiles,
            DelimiterTemplate: string.Empty,
            TwoBlankLines: false,
            IncludeTableOfContents: false,
            IncludeManifest: false,
            PreserveFolderStructure: false,
            IncludeHeaders: false,
            IncludeCodeFences: false);
    }

    // ------------------------------------------------------------------
    // F-019 temp-file-then-rename tests
    // ------------------------------------------------------------------

    /// <summary>
    /// An IFormatWriter that throws IOException on WriteAsync, to test
    /// cleanup when the writer fails mid-write.
    /// </summary>
    private sealed class ThrowingFormatWriter : IFormatWriter
    {
        public string Extension => ".stub";
        public string DisplayName => "Throwing";
        public string MimeType => "application/octet-stream";
        public bool SupportsStreaming => true;

        public Task WriteAsync(
            ComposedDocument document,
            Stream output,
            CancellationToken cancellationToken = default)
        {
            _ = document;
            _ = output;
            _ = cancellationToken;
            throw new IOException("simulated writer failure");
        }
    }

    /// <summary>
    /// An IFormatWriter that cancels the supplied CancellationTokenSource
    /// after writing the first file, so the second file's
    /// ThrowIfCancellationRequested fires. Deterministic cancellation
    /// mid-write.
    /// </summary>
    private sealed class CancellingFormatWriter : IFormatWriter
    {
        private readonly CancellationTokenSource _cts;
        private int _filesWritten;

        public CancellingFormatWriter(CancellationTokenSource cts)
        {
            _cts = cts;
        }

        public string Extension => ".stub";
        public string DisplayName => "Cancelling";
        public string MimeType => "application/octet-stream";
        public bool SupportsStreaming => true;

        public async Task WriteAsync(
            ComposedDocument document,
            Stream output,
            CancellationToken cancellationToken = default)
        {
            foreach (var fc in document.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await output.WriteAsync(fc.RawBytes, cancellationToken).ConfigureAwait(false);
                _filesWritten++;
                if (_filesWritten == 1)
                {
                    _cts.Cancel();
                }
            }
        }
    }

    /// <summary>
    /// Constructs an OutputSink over a supplied writer.
    /// </summary>
    private static OutputSink BuildSinkWithWriter(
        IResilienceExecutor executor,
        IOverwriteConfirmer confirmer,
        IFormatWriter writer)
    {
        var registry = new FormatWriterRegistry(new IFormatWriter[] { writer });
        var paths = new DefaultPathsProvider();
        return new OutputSink(registry, paths, executor, confirmer);
    }

    [Fact]
    public async Task MergedWriteLeavesNoTempFileOnSuccess()
    {
        using var provider = BuildProviderWithPolicies();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var confirmer = new StubOverwriteConfirmer();
        var sink = BuildSink(executor, confirmer);

        var sourcePath = Path.Combine(_tempRoot, "src-tmp-1.txt");
        var sourceBytes = Encoding.UTF8.GetBytes("temp-cleanup-success");
        File.WriteAllBytes(sourcePath, sourceBytes);

        var document = MakeSingleMergedDocument(sourcePath, sourceBytes);
        var options = new OutputSinkOptions(
            DestinationFolder: _tempRoot,
            Extension: ".stub",
            FileName: "out-tmp-1");

        var result = await sink.SaveAsync(document, options);

        result.Success.Should().BeTrue();
        var outPath = Path.Combine(_tempRoot, "out-tmp-1.stub");
        File.Exists(outPath).Should().BeTrue();
        Directory.EnumerateFiles(_tempRoot, "*.tmp-*").Should().BeEmpty(
            "no temp file may remain after a successful write");
    }

    [Fact]
    public async Task SeparateWriteLeavesNoTempFileOnSuccess()
    {
        using var provider = BuildProviderWithPolicies();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var confirmer = new StubOverwriteConfirmer();
        var sink = BuildSink(executor, confirmer);

        var files = new List<FileContent>();
        for (var i = 0; i < 3; i++)
        {
            var p = Path.Combine(_tempRoot, $"src-sep-{i}.txt");
            var b = Encoding.UTF8.GetBytes($"separate content {i}");
            File.WriteAllBytes(p, b);
            files.Add(MakeFileContent(p, b, i));
        }

        var document = MakeSeparateDocument(files);
        var options = new OutputSinkOptions(
            DestinationFolder: _tempRoot,
            Extension: ".stub",
            FileName: null);

        var result = await sink.SaveAsync(document, options);

        result.Success.Should().BeTrue();
        Directory.EnumerateFiles(_tempRoot, "*.tmp-*").Should().BeEmpty(
            "no temp file may remain after a successful separate write");
    }

    [Fact]
    public async Task MergedWriteFailureLeavesNoFinalFileAndNoTemp()
    {
        using var provider = BuildProviderWithPolicies();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var confirmer = new StubOverwriteConfirmer();
        var sink = BuildSinkWithWriter(executor, confirmer, new ThrowingFormatWriter());

        var sourcePath = Path.Combine(_tempRoot, "src-tmp-3.txt");
        var sourceBytes = Encoding.UTF8.GetBytes("temp-cleanup-failure");
        File.WriteAllBytes(sourcePath, sourceBytes);

        var document = MakeSingleMergedDocument(sourcePath, sourceBytes);
        var options = new OutputSinkOptions(
            DestinationFolder: _tempRoot,
            Extension: ".stub",
            FileName: "out-tmp-3");

        var result = await sink.SaveAsync(document, options);

        result.Success.Should().BeFalse();
        var outPath = Path.Combine(_tempRoot, "out-tmp-3.stub");
        File.Exists(outPath).Should().BeFalse(
            "a failed write must not leave a final file");
        Directory.EnumerateFiles(_tempRoot, "*.tmp-*").Should().BeEmpty(
            "a failed write must not leave a temp file");
    }

    [Fact]
    public async Task MergedWriteCancellationLeavesNoFinalFileAndNoTemp()
    {
        using var provider = BuildProviderWithPolicies();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var confirmer = new StubOverwriteConfirmer();

        using var cts = new CancellationTokenSource();
        var sink = BuildSinkWithWriter(executor, confirmer, new CancellingFormatWriter(cts));

        var files = new List<FileContent>();
        for (var i = 0; i < 2; i++)
        {
            var p = Path.Combine(_tempRoot, $"src-tmp-4-{i}.txt");
            var b = Encoding.UTF8.GetBytes($"cancel content {i}");
            File.WriteAllBytes(p, b);
            files.Add(MakeFileContent(p, b, i));
        }

        var document = MakeSeparateDocument(files);
        var options = new OutputSinkOptions(
            DestinationFolder: _tempRoot,
            Extension: ".stub",
            FileName: null);

        var result = await sink.SaveAsync(document, options, cancellationToken: cts.Token);

        result.Success.Should().BeFalse();
        Directory.EnumerateFiles(_tempRoot, "*.tmp-*").Should().BeEmpty(
            "a cancelled write must not leave a temp file");
    }

    [Fact]
    public async Task MergedWriteReplacesExistingFileWhenConfirmed()
    {
        using var provider = BuildProviderWithPolicies();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var confirmer = new StubOverwriteConfirmer();
        confirmer.Enqueue(OverwriteDecision.Replace, applyToAll: false);
        var sink = BuildSink(executor, confirmer);

        var sourcePath = Path.Combine(_tempRoot, "src-tmp-5.txt");
        var sourceBytes = Encoding.UTF8.GetBytes("replacement via temp");
        File.WriteAllBytes(sourcePath, sourceBytes);

        var outPath = Path.Combine(_tempRoot, "out-tmp-5.stub");
        File.WriteAllBytes(outPath, Encoding.UTF8.GetBytes("old content to be replaced"));

        var document = MakeSingleMergedDocument(sourcePath, sourceBytes);
        var options = new OutputSinkOptions(
            DestinationFolder: _tempRoot,
            Extension: ".stub",
            FileName: "out-tmp-5");

        var result = await sink.SaveAsync(document, options);

        result.Success.Should().BeTrue();
        confirmer.PromptCount.Should().Be(1);
        File.ReadAllBytes(outPath).Should().Equal(sourceBytes,
            "the existing file must be replaced with the new bytes");
        Directory.EnumerateFiles(_tempRoot, "*.tmp-*").Should().BeEmpty(
            "no temp file may remain after a replace write");
    }

    [Fact]
    public async Task MergedWriteKeepsExistingFileWhenCancelled()
    {
        using var provider = BuildProviderWithPolicies();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var confirmer = new StubOverwriteConfirmer();
        confirmer.Enqueue(OverwriteDecision.Cancel, applyToAll: false);
        var sink = BuildSink(executor, confirmer);

        var sourcePath = Path.Combine(_tempRoot, "src-tmp-6.txt");
        var sourceBytes = Encoding.UTF8.GetBytes("should not be written");
        File.WriteAllBytes(sourcePath, sourceBytes);

        var outPath = Path.Combine(_tempRoot, "out-tmp-6.stub");
        var existingBytes = Encoding.UTF8.GetBytes("existing untouched content");
        File.WriteAllBytes(outPath, existingBytes);

        var document = MakeSingleMergedDocument(sourcePath, sourceBytes);
        var options = new OutputSinkOptions(
            DestinationFolder: _tempRoot,
            Extension: ".stub",
            FileName: "out-tmp-6");

        var result = await sink.SaveAsync(document, options);

        result.Success.Should().BeFalse();
        confirmer.PromptCount.Should().Be(1);
        File.ReadAllBytes(outPath).Should().Equal(existingBytes,
            "the existing file must be untouched after Cancel");
        Directory.EnumerateFiles(_tempRoot, "*.tmp-*").Should().BeEmpty(
            "a cancelled confirmation must not leave a temp file");
    }
}
