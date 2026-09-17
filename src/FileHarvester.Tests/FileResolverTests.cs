// ============================================================================
// FILE:     src/FileHarvester.Tests/FileResolverTests.cs
// PURPOSE:  Unit tests for FileResolver's four-tier resolution algorithm.
//           Uses the shared TestHarness for the temp directory and DI.
// RELATION: Consumes TestHarness (Infrastructure) and IFileResolver.
//           Proves the resolver and the resilience pipeline work together.
// ============================================================================

using FileHarvester.Core.Domain;
using FileHarvester.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests;

/// <summary>
/// Unit tests for <see cref="IFileResolver"/> via the real DI pipeline.
/// </summary>
public sealed class FileResolverTests : IClassFixture<TestHarness>
{
    private readonly TestHarness _harness;

    public FileResolverTests(TestHarness harness)
    {
        _harness = harness;
    }

    // ---------------------------------------------------------------------
    // TEST 1 — Absolute path to an existing file resolves as Exact
    // ---------------------------------------------------------------------
    [Fact]
    public async Task AbsolutePathExistingFileResolvesAsExact()
    {
        var path = _harness.WriteFile("absolute-test.txt", "hello");

        var requests = new[] { TestHarness.Request(path) };

        var resolved = await _harness.Resolver.ResolveAsync(requests);

        resolved.Should().HaveCount(1);
        resolved[0].AbsolutePath.Should().Be(path);
        resolved[0].Confidence.Should().Be(ResolutionConfidence.Exact);
        resolved[0].SizeBytes.Should().Be(5);
        resolved[0].Request.RawInput.Should().Be(path);
    }

    // ---------------------------------------------------------------------
    // TEST 2 — Absolute path to a missing file resolves as NotFound
    // ---------------------------------------------------------------------
    [Fact]
    public async Task AbsolutePathMissingFileResolvesAsNotFound()
    {
        var missing = Path.Combine(_harness.TempRoot, "does-not-exist.txt");

        var requests = new[] { TestHarness.Request(missing) };

        var resolved = await _harness.Resolver.ResolveAsync(requests);

        resolved.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------
    // TEST 3 — Relative path against a hinted directory resolves as Relative
    // ---------------------------------------------------------------------
    [Fact]
    public async Task RelativePathAgainstHintResolvesAsRelative()
    {
        var path = _harness.WriteFile("hint/hinted.txt", "hinted content");
        var hint = Path.GetDirectoryName(path)!;

        var requests = new[]
        {
            new FileRequest(
                Index: 0,
                RawInput: "hinted.txt",
                HintedDirectory: hint),
        };

        var resolved = await _harness.Resolver.ResolveAsync(requests);

        resolved.Should().HaveCount(1);
        resolved[0].AbsolutePath.Should().Be(path);
        resolved[0].Confidence.Should().Be(ResolutionConfidence.Relative);
    }

    // ---------------------------------------------------------------------
    // TEST 4 — Bare filename not found anywhere resolves as NotFound
    // ---------------------------------------------------------------------
    [Fact]
    public async Task BareFilenameNotFoundResolvesAsNotFound()
    {
        var requests = new[]
        {
            TestHarness.Request("absolutely-no-such-file-xyz-12345.bin"),
        };

        var resolved = await _harness.Resolver.ResolveAsync(requests);

        resolved.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------
    // TEST 5 — Multiple requests resolve independently in one batch
    // ---------------------------------------------------------------------
    [Fact]
    public async Task MultipleRequestsResolveIndependentlyInOneBatch()
    {
        var fileA = _harness.WriteFile("batch-a.txt", "AAA");
        var fileB = _harness.WriteFile("batch-b.txt", "BBBBB");
        var missing = Path.Combine(_harness.TempRoot, "batch-missing.txt");

        var requests = new[]
        {
            TestHarness.Request(fileA, index: 0),
            TestHarness.Request(missing, index: 1),
            TestHarness.Request(fileB, index: 2),
        };

        var resolved = await _harness.Resolver.ResolveAsync(requests);

        resolved.Should().HaveCount(2);
        resolved.Should().Contain(r => r.AbsolutePath == fileA);
        resolved.Should().Contain(r => r.AbsolutePath == fileB);
    }

    // ---------------------------------------------------------------------
    // TEST 6 — Progress is reported once per request
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ProgressIsReportedOncePerRequest()
    {
        var fileA = _harness.WriteFile("progress-a.txt", "a");
        var fileB = _harness.WriteFile("progress-b.txt", "b");

        var requests = new[]
        {
            TestHarness.Request(fileA, index: 0),
            TestHarness.Request(fileB, index: 1),
        };

        var ticks = new List<ResolveProgress>();
        var progress = new SyncProgress<ResolveProgress>(p => ticks.Add(p));

        await _harness.Resolver.ResolveAsync(requests, progress);

        ticks.Should().HaveCount(2);
        ticks.Select(t => t.Processed).Should().Equal(1, 2);
        ticks.Select(t => t.Total).Should().AllBeEquivalentTo(2);
        ticks.Should().OnlyContain(t => t.Status == FileStatus.Found);
    }
}
