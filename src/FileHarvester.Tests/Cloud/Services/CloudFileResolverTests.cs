// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Services/CloudFileResolverTests.cs
// PURPOSE:  Unit tests for CloudFileResolver. Uses StubCloudProvider.
//           Verifies path classification, the exists-then-metadata
//           contract, and the unsupported-scheme note.
// RELATION: Consumes CloudFileResolver and StubCloudProvider. No I/O,
//           no network, no resilience.
// ============================================================================

using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Services;
using FileHarvester.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud.Services;

/// <summary>
/// Tests for <see cref="CloudFileResolver"/>.
/// </summary>
public sealed class CloudFileResolverTests
{
    private static (CloudFileResolver Resolver, StubCloudProvider Provider) Build(
        PathSchemeKind kind = PathSchemeKind.S3)
    {
        var provider = new StubCloudProvider();
        var resolver = new CloudFileResolver((k, _, _) => Task.FromResult(k == kind ? CloudProviderResolution.Resolved(provider) : CloudProviderResolution.Failed(CloudResolutionFailure.NoProviderRegistered)));
        return (resolver, provider);
    }

    [Fact]
    public async Task ResolveAsyncIgnoresLocalRequests()
    {
        var (resolver, provider) = Build();
        var requests = new[]
        {
            new FileRequest(Index: 0, RawInput: @"C:\Users\HP\file.txt"),
        };

        var resolved = await resolver.ResolveAsync(requests);

        resolved.Should().BeEmpty();
        provider.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsyncResolvesExistingCloudPath()
    {
        var (resolver, provider) = Build();
        provider.OnExists = _ => Task.FromResult(true);
        provider.OnGetMetadata = _ => Task.FromResult(new ProviderMetadata(
            SizeBytes: 1234,
            LastModifiedUtc: new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc),
            ContentType: "text/plain",
            ETag: "abc",
            Extra: new Dictionary<string, string>()));

        var requests = new[]
        {
            new FileRequest(Index: 0, RawInput: "s3://bucket/docs/a.txt"),
        };

        var resolved = await resolver.ResolveAsync(requests);

        resolved.Should().HaveCount(1);
        resolved[0].AbsolutePath.Should().Be("s3://bucket/docs/a.txt");
        resolved[0].SizeBytes.Should().Be(1234);
        resolved[0].Confidence.Should().Be(ResolutionConfidence.Searched);

        provider.Calls.Should().HaveCount(2);
        provider.Calls[0].Should().StartWith("ExistsAsync:");
        provider.Calls[1].Should().StartWith("GetMetadataAsync:");
    }

    [Fact]
    public async Task ResolveAsyncSkipsMissingCloudPath()
    {
        var (resolver, provider) = Build();
        provider.OnExists = _ => Task.FromResult(false);
        // OnGetMetadata deliberately unset: if the resolver calls it,
        // the stub throws NotSupportedException and the test fails.

        var requests = new[]
        {
            new FileRequest(Index: 0, RawInput: "s3://bucket/missing.txt"),
        };

        var resolved = await resolver.ResolveAsync(requests);

        resolved.Should().BeEmpty();
        provider.Calls.Should().ContainSingle();
        provider.Calls[0].Should().StartWith("ExistsAsync:");
    }

    [Fact]
    public async Task ResolveAsyncReportsProgressPerRequest()
    {
        var (resolver, provider) = Build();
        provider.OnExists = _ => Task.FromResult(true);
        provider.OnGetMetadata = _ => Task.FromResult(new ProviderMetadata(
            SizeBytes: 100,
            LastModifiedUtc: DateTime.UtcNow,
            ContentType: null,
            ETag: null,
            Extra: new Dictionary<string, string>()));

        var requests = new[]
        {
            new FileRequest(Index: 0, RawInput: "s3://bucket/a.txt"),
            new FileRequest(Index: 1, RawInput: "s3://bucket/b.txt"),
        };

        var ticks = new List<ResolveProgress>();
        var progress = new SyncProgress<ResolveProgress>(p => ticks.Add(p));

        await resolver.ResolveAsync(requests, progress);

        ticks.Should().HaveCount(2);
        ticks[0].Processed.Should().Be(1);
        ticks[1].Processed.Should().Be(2);
        ticks.Should().OnlyContain(t => t.Status == FileStatus.Found);
        ticks.Should().OnlyContain(t => t.Note == null);
    }

    [Fact]
    public async Task ResolveAsyncReportsUnsupportedSchemeWithNote()
    {
        // Factory returns null for everything — no provider registered.
        var resolver = new CloudFileResolver((_, _, _) => Task.FromResult(CloudProviderResolution.Failed(CloudResolutionFailure.NoProviderRegistered)));

        var requests = new[]
        {
            new FileRequest(Index: 0, RawInput: "gs://bucket/key"),
        };

        var ticks = new List<ResolveProgress>();
        var progress = new SyncProgress<ResolveProgress>(p => ticks.Add(p));

        var resolved = await resolver.ResolveAsync(requests, progress);

        resolved.Should().BeEmpty();
        ticks.Should().HaveCount(1);
        ticks[0].Status.Should().Be(FileStatus.NotFound);
        ticks[0].Note.Should().NotBeNullOrWhiteSpace();
        ticks[0].Note.Should().Contain("gs");
    }

    [Fact]
    public async Task ResolveAsyncCancellationPropagates()
    {
        var (resolver, _) = Build();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var requests = new[]
        {
            new FileRequest(Index: 0, RawInput: "s3://bucket/a.txt"),
        };

        var act = async () => await resolver.ResolveAsync(requests, null, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
