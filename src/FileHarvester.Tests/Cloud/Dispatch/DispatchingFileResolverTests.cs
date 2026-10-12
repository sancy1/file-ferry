// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Dispatch/DispatchingFileResolverTests.cs
// PURPOSE:  Unit tests for DispatchingFileResolver. Uses local stubs
//           and a stub cloud resolver. Verifies routing, order, and
//           progress.
// ============================================================================

using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Dispatch;
using FileHarvester.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud.Dispatch;

public sealed class DispatchingFileResolverTests
{
    private static ResolvedFile MakeResolved(string path, int index)
    {
        var req = new FileRequest(Index: index, RawInput: path);
        return new ResolvedFile(
            Request: req,
            AbsolutePath: path,
            SizeBytes: 1,
            LastModifiedUtc: DateTime.UtcNow,
            Confidence: ResolutionConfidence.Searched);
    }

    [Fact]
    public async Task ResolveEmptyBatchReturnsEmpty()
    {
        var local = new StubLocalFileResolver();
        var cloud = new StubCloudFileResolver();
        var sut = new DispatchingFileResolver(local, cloud);

        var result = await sut.ResolveAsync(Array.Empty<FileRequest>());

        result.Should().BeEmpty();
        local.Batches.Should().BeEmpty();
        cloud.Batches.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveLocalOnlyCallsLocalResolverOnly()
    {
        var local = new StubLocalFileResolver();
        var cloud = new StubCloudFileResolver();
        local.OnResolve = rs => Task.FromResult<IReadOnlyList<ResolvedFile>>(
            rs.Select((r, i) => MakeResolved(r.RawInput, r.Index)).ToList());
        var sut = new DispatchingFileResolver(local, cloud);

        var requests = new[]
        {
            new FileRequest(Index: 0, RawInput: @"C:\a.txt"),
            new FileRequest(Index: 1, RawInput: @"C:\b.txt"),
        };

        var result = await sut.ResolveAsync(requests);

        result.Should().HaveCount(2);
        local.Batches.Should().HaveCount(1);
        cloud.Batches.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveCloudOnlyCallsCloudResolverOnly()
    {
        var local = new StubLocalFileResolver();
        var cloud = new StubCloudFileResolver();
        cloud.OnResolve = rs => Task.FromResult<IReadOnlyList<ResolvedFile>>(
            rs.Select(r => MakeResolved(r.RawInput, r.Index)).ToList());
        var sut = new DispatchingFileResolver(local, cloud);

        var requests = new[]
        {
            new FileRequest(Index: 0, RawInput: "s3://bucket/a.txt"),
            new FileRequest(Index: 1, RawInput: "s3://bucket/b.txt"),
        };

        var result = await sut.ResolveAsync(requests);

        result.Should().HaveCount(2);
        local.Batches.Should().BeEmpty();
        cloud.Batches.Should().HaveCount(1);
    }

    [Fact]
    public async Task ResolveMixedBatchMergesInOriginalOrder()
    {
        var local = new StubLocalFileResolver();
        var cloud = new StubCloudFileResolver();

        local.OnResolve = rs => Task.FromResult<IReadOnlyList<ResolvedFile>>(
            rs.Select(r => MakeResolved(r.RawInput, r.Index)).ToList());
        cloud.OnResolve = rs => Task.FromResult<IReadOnlyList<ResolvedFile>>(
            rs.Select(r => MakeResolved(r.RawInput, r.Index)).ToList());

        var sut = new DispatchingFileResolver(local, cloud);

        var requests = new[]
        {
            new FileRequest(Index: 0, RawInput: @"C:\a.txt"),
            new FileRequest(Index: 1, RawInput: "s3://bucket/b.txt"),
            new FileRequest(Index: 2, RawInput: @"C:\c.txt"),
        };

        var result = await sut.ResolveAsync(requests);

        result.Should().HaveCount(3);
        result[0].Request.Index.Should().Be(0);
        result[1].Request.Index.Should().Be(1);
        result[2].Request.Index.Should().Be(2);
    }

    [Fact]
    public async Task ResolveMixedBatchReportsProgressPerPhase()
    {
        var local = new StubLocalFileResolver();
        var cloud = new StubCloudFileResolver();
        local.OnResolve = rs => Task.FromResult<IReadOnlyList<ResolvedFile>>(
            rs.Select(r => MakeResolved(r.RawInput, r.Index)).ToList());
        cloud.OnResolve = rs => Task.FromResult<IReadOnlyList<ResolvedFile>>(
            rs.Select(r => MakeResolved(r.RawInput, r.Index)).ToList());

        var sut = new DispatchingFileResolver(local, cloud);

        var requests = new[]
        {
            new FileRequest(Index: 0, RawInput: @"C:\a.txt"),
            new FileRequest(Index: 1, RawInput: "s3://bucket/b.txt"),
        };

        var ticks = new List<ResolveProgress>();
        var progress = new SyncProgress<ResolveProgress>(p => ticks.Add(p));

        await sut.ResolveAsync(requests, progress);

        ticks.Should().HaveCount(2);
        ticks.Should().OnlyContain(t => t.Total == 2);
        ticks[0].Processed.Should().Be(1);
        ticks[1].Processed.Should().Be(2);
    }
}
