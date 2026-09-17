// ============================================================================
// FILE:     src/FileHarvester.Tests/PipelineIntegrationTests.cs
// PURPOSE:  End-to-end integration tests for the three-stage harvest pipeline
//           (resolve → read → compose). Uses the shared TestHarness.
// RELATION: Consumes TestHarness (Infrastructure) plus IFileResolver,
//           IContentReader, and IBundleComposer. Exit criterion for Phase 1.
// ============================================================================

using FileHarvester.Core.Domain;
using FileHarvester.Core.Implementation;
using FileHarvester.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests;

/// <summary>
/// End-to-end tests for the resolve → read → compose pipeline.
/// </summary>
public sealed class PipelineIntegrationTests : IClassFixture<TestHarness>
{
    private readonly TestHarness _harness;

    private static readonly string[] ExpectedSeparateTexts = { "first", "second" };

    public PipelineIntegrationTests(TestHarness harness)
    {
        _harness = harness;
    }

    // ---------------------------------------------------------------------
    // TEST 1 — Full pipeline produces a correct bundle
    // ---------------------------------------------------------------------
    [Fact]
    public async Task FullPipelineProducesCorrectBundle()
    {
        var pathA = _harness.WriteFile("a.txt", "AAA");
        var pathB = _harness.WriteFile("b.txt", "BBB");
        var pathC = _harness.WriteFile("sub/c.txt", "CCC");

        var requests = new[]
        {
            TestHarness.Request(pathA, index: 0),
            TestHarness.Request(pathB, index: 1),
            TestHarness.Request(pathC, index: 2),
        };

        var resolved = await _harness.Resolver.ResolveAsync(requests);
        var contents = await _harness.Reader.ReadManyAsync(resolved);
        var document = _harness.Composer.Compose(
            contents,
            ComposerOptions.SingleMerged());

        var text = BundleComposer.RenderMergedText(document);

        text.Should().Contain("===== " + pathA + " =====");
        text.Should().Contain("===== " + pathB + " =====");
        text.Should().Contain("===== " + pathC + " =====");
        text.Should().Contain("AAA");
        text.Should().Contain("BBB");
        text.Should().Contain("CCC");
    }

    // ---------------------------------------------------------------------
    // TEST 2 — Separate files mode preserves all file contents
    // ---------------------------------------------------------------------
    [Fact]
    public async Task SeparateFilesModePreservesAllFiles()
    {
        var pathA = _harness.WriteFile("sep-a.txt", "first");
        var pathB = _harness.WriteFile("sep-b.txt", "second");

        var requests = new[]
        {
            TestHarness.Request(pathA, index: 0),
            TestHarness.Request(pathB, index: 1),
        };

        var resolved = await _harness.Resolver.ResolveAsync(requests);
        var contents = await _harness.Reader.ReadManyAsync(resolved);
        var document = _harness.Composer.Compose(
            contents,
            ComposerOptions.SeparateFiles());

        document.Mode.Should().Be(OutputMode.SeparateFiles);
        document.Files.Should().HaveCount(2);
        document.Files.Select(f => f.DecodedText)
            .Should().BeEquivalentTo(ExpectedSeparateTexts);
    }

    // ---------------------------------------------------------------------
    // TEST 3 — Mixed batch survives partial failures
    // ---------------------------------------------------------------------
    [Fact]
    public async Task MixedBatchSurvivesPartialFailures()
    {
        var existing = _harness.WriteFile("present.txt", "content");
        var missing  = Path.Combine(_harness.TempRoot, "not-there.txt");

        var requests = new[]
        {
            TestHarness.Request(existing, index: 0),
            TestHarness.Request(missing,  index: 1),
        };

        var resolved = await _harness.Resolver.ResolveAsync(requests);

        resolved.Should().HaveCount(1);
        resolved[0].AbsolutePath.Should().Be(existing);

        var contents = await _harness.Reader.ReadManyAsync(resolved);
        var document = _harness.Composer.Compose(
            contents,
            ComposerOptions.SingleMerged());

        document.Files.Should().HaveCount(1);
        document.Files[0].DecodedText.Should().Be("content");
    }

    // ---------------------------------------------------------------------
    // TEST 4 — Unicode and encoding fidelity through the full pipeline
    // ---------------------------------------------------------------------
    [Fact]
    public async Task UnicodeFidelityThroughPipeline()
    {
        var text = "Café • Überall • 日本語 • ñ";
        var path = _harness.WriteFile("unicode.txt", text);

        var requests = new[] { TestHarness.Request(path) };
        var resolved = await _harness.Resolver.ResolveAsync(requests);
        var contents = await _harness.Reader.ReadManyAsync(resolved);

        contents.Should().HaveCount(1);
        contents[0].DecodedText.Should().Be(text);
    }

    // ---------------------------------------------------------------------
    // TEST 5 — Custom delimiter expands through the pipeline
    // ---------------------------------------------------------------------
    [Fact]
    public async Task CustomDelimiterExpands()
    {
        var path = _harness.WriteFile("custom.txt", "body");
        var requests = new[] { TestHarness.Request(path) };

        var resolved = await _harness.Resolver.ResolveAsync(requests);
        var contents = await _harness.Reader.ReadManyAsync(resolved);

        var options = ComposerOptions.SingleMerged() with
        {
            DelimiterTemplate = ">> {filename} <<",
        };

        var document = _harness.Composer.Compose(contents, options);
        var text = BundleComposer.RenderMergedText(document);

        text.Should().Contain(">> custom.txt <<");
        text.Should().Contain("body");
    }

    // ---------------------------------------------------------------------
    // TEST 6 — Both stages report progress
    // ---------------------------------------------------------------------
    [Fact]
    public async Task BothStagesReportProgress()
    {
        var pathA = _harness.WriteFile("prog-a.txt", "a");
        var pathB = _harness.WriteFile("prog-b.txt", "b");

        var requests = new[]
        {
            TestHarness.Request(pathA, index: 0),
            TestHarness.Request(pathB, index: 1),
        };

        var resolveTicks = new List<ResolveProgress>();
        var readTicks = new List<ReadProgress>();

        var resolved = await _harness.Resolver.ResolveAsync(
            requests,
            new SyncProgress<ResolveProgress>(p => resolveTicks.Add(p)));

        await _harness.Reader.ReadManyAsync(
            resolved,
            new SyncProgress<ReadProgress>(p => readTicks.Add(p)));

        resolveTicks.Should().HaveCount(2);
        readTicks.Should().HaveCount(2);

        resolveTicks.Last().Processed.Should().Be(2);
        readTicks.Last().Processed.Should().Be(2);
    }

    // ---------------------------------------------------------------------
    // TEST 7 — Batch of 50 files resolves, reads, and composes cleanly
    // ---------------------------------------------------------------------
    [Fact]
    public async Task BatchOfFiftyFilesFlowsCleanly()
    {
        var requests = new List<FileRequest>();
        for (var i = 0; i < 50; i++)
        {
            var path = _harness.WriteFile($"batch/f{i:D2}.txt", $"content-{i}");
            requests.Add(TestHarness.Request(path, index: i));
        }

        var resolved = await _harness.Resolver.ResolveAsync(requests);
        var contents = await _harness.Reader.ReadManyAsync(resolved);
        var document = _harness.Composer.Compose(
            contents,
            ComposerOptions.SingleMerged());

        resolved.Should().HaveCount(50);
        contents.Should().HaveCount(50);
        document.Files.Should().HaveCount(50);

        var text = BundleComposer.RenderMergedText(document);
        text.Should().Contain("content-0");
        text.Should().Contain("content-49");
    }
}
