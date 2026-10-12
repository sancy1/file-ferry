// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Providers/DropboxProviderTests.cs
// PURPOSE:  Consumer-driven tests for DropboxProvider. Uses a substitute
//           IDropboxClient and a real IResilienceExecutor. No Dropbox SDK,
//           no Dropbox account, no browser, no network call.
// RELATION: Exercises DropboxProvider only. DropboxClientAdapter has no
//           substitute seam and its direct tests are deferred - see F-142
//           for the WriteMode.Overwrite replace/create boundary.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Providers;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Extensions;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace FileHarvester.Tests.Cloud.Providers;

/// <summary>Tests for DropboxProvider.</summary>
public sealed class DropboxProviderTests
{
    private const string FilePath = "/folder/file.txt";

    private static DropboxObjectMetadata Meta(
        string id = "test-id",
        string pathLower = "/folder/file.txt",
        string name = "file.txt",
        string? rev = "rev-value",
        long size = 1234,
        DateTime? modified = null,
        string? hash = "hash-value",
        bool isFolder = false)
        => new(id, pathLower, name, rev, size, modified, hash, isFolder);

    private static DropboxProvider BuildProvider(
        IDropboxClient client,
        IResilienceExecutor executor)
        => new(client, executor);

    private static IResilienceExecutor RealExecutor()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddPortfolioResilience(_ => { });
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IResilienceExecutor>();
    }

    [Fact]
    public async Task OpenReadAsyncParsesTripleSlashPath()
    {
        var client = Substitute.For<IDropboxClient>();
        client.DownloadAsync(FilePath, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream()));
        var provider = BuildProvider(client, RealExecutor());

        await using var stream = await provider.OpenReadAsync("dropbox:///folder/file.txt");

        await client.Received(1).DownloadAsync(
            "/folder/file.txt",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenReadAsyncNormalizesMissingLeadingSlash()
    {
        var client = Substitute.For<IDropboxClient>();
        client.DownloadAsync(FilePath, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream()));
        var provider = BuildProvider(client, RealExecutor());

        await using var stream = await provider.OpenReadAsync("dropbox://folder/file.txt");

        await client.Received(1).DownloadAsync(
            "/folder/file.txt",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenReadAsyncStripsTheInstanceSegment()
    {
        var client = Substitute.For<IDropboxClient>();
        client.DownloadAsync(FilePath, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream()));
        var provider = BuildProvider(client, RealExecutor());

        await using var stream = await provider.OpenReadAsync("dropbox://[work]/folder/file.txt");

        await client.Received(1).DownloadAsync(
            "/folder/file.txt",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenReadAsyncRejectsNonDropboxScheme()
    {
        var client = Substitute.For<IDropboxClient>();
        var provider = BuildProvider(client, RealExecutor());

        var act = async () => await provider.OpenReadAsync("s3://bucket/key");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task OpenReadAsyncRejectsDropboxWithNoPath()
    {
        var client = Substitute.For<IDropboxClient>();
        var provider = BuildProvider(client, RealExecutor());

        var act = async () => await provider.OpenReadAsync("dropbox://");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task OpenReadAsyncReturnsTheStream()
    {
        var payload = new byte[] { 1, 2, 3 };
        var client = Substitute.For<IDropboxClient>();
        client.DownloadAsync(FilePath, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream(payload)));
        var provider = BuildProvider(client, RealExecutor());

        await using var stream = await provider.OpenReadAsync("dropbox:///folder/file.txt");
        var buffer = new byte[3];
        var read = await stream.ReadAsync(buffer.AsMemory(), CancellationToken.None);

        read.Should().Be(3);
        buffer.Should().Equal(payload);
    }

    [Fact]
    public async Task ListAsyncParsesPathAndReturnsPage()
    {
        var client = Substitute.For<IDropboxClient>();
        client.ListAsync("/folder", null, 100, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DropboxObjectPage(
                new[] { Meta("a"), Meta("b") }, null)));
        var provider = BuildProvider(client, RealExecutor());

        var page = await provider.ListAsync("dropbox:///folder", null, 100);

        page.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListAsyncPassesContinuationToken()
    {
        var client = Substitute.For<IDropboxClient>();
        client.ListAsync(Arg.Any<string>(), "cursor-abc", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DropboxObjectPage(
                Array.Empty<DropboxObjectMetadata>(), "next-cursor")));
        var provider = BuildProvider(client, RealExecutor());

        var page = await provider.ListAsync("dropbox:///folder", "cursor-abc", 100);

        page.ContinuationToken.Should().Be("next-cursor");
    }

    [Fact]
    public async Task ListAsyncMarksFoldersAsDirectories()
    {
        var client = Substitute.For<IDropboxClient>();
        client.ListAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DropboxObjectPage(
                new[] { Meta("a", isFolder: true) }, null)));
        var provider = BuildProvider(client, RealExecutor());

        var page = await provider.ListAsync("dropbox:///folder", null, 100);

        page.Entries[0].IsDirectory.Should().BeTrue();
    }

    [Fact]
    public async Task ListAsyncBuildsDropboxUriForEntries()
    {
        var client = Substitute.For<IDropboxClient>();
        client.ListAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DropboxObjectPage(
                new[] { Meta("a", pathLower: "/folder/file.txt") }, null)));
        var provider = BuildProvider(client, RealExecutor());

        var page = await provider.ListAsync("dropbox:///folder", null, 100);

        page.Entries[0].Path.Should().Be("dropbox:///folder/file.txt");
    }

    private static IResilienceExecutor SubstituteExecutor()
    {
        var executor = Substitute.For<IResilienceExecutor>();
        executor.ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return executor;
    }

    [Fact]
    public async Task OpenWriteAsyncCompleteCallsUpdateWithExactPath()
    {
        var client = Substitute.For<IDropboxClient>();
        client.UpdateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var provider = BuildProvider(client, RealExecutor());

        var handle = await provider.OpenWriteAsync("dropbox:///folder/file.txt", null);
        await handle.Stream.WriteAsync(new byte[] { 1, 2, 3 });
        await handle.CompleteAsync();

        await client.Received(1).UpdateAsync(
            "/folder/file.txt",
            Arg.Any<string?>(),
            Arg.Any<Stream>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenWriteAsyncWithoutCompleteDoesNotWrite()
    {
        var client = Substitute.For<IDropboxClient>();
        var provider = BuildProvider(client, RealExecutor());

        var handle = await provider.OpenWriteAsync("dropbox:///folder/file.txt", null);
        await handle.Stream.WriteAsync(new byte[] { 1, 2, 3 });
        await handle.DisposeAsync();

        await client.DidNotReceive().UpdateAsync(
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<Stream>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenWriteAsyncPassesTheContentType()
    {
        var client = Substitute.For<IDropboxClient>();
        client.UpdateAsync(Arg.Any<string>(), "text/plain", Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var provider = BuildProvider(client, RealExecutor());

        var handle = await provider.OpenWriteAsync("dropbox:///folder/file.txt", "text/plain");
        await handle.Stream.WriteAsync(new byte[] { 1, 2, 3 });
        await handle.CompleteAsync();

        await client.Received(1).UpdateAsync(
            Arg.Any<string>(),
            "text/plain",
            Arg.Any<Stream>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenWriteAsyncCompleteRoutesThroughCloudWrite()
    {
        var client = Substitute.For<IDropboxClient>();
        var executor = SubstituteExecutor();
        var provider = BuildProvider(client, executor);

        var handle = await provider.OpenWriteAsync("dropbox:///folder/file.txt", null);
        await handle.Stream.WriteAsync(new byte[] { 1, 2, 3 });
        await handle.CompleteAsync();

        await executor.Received(1).ExecuteAsync(
            FileHarvester.Core.Composition.PolicyNames.CloudWrite,
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExistsAsyncReturnsTrueWhenPresent()
    {
        var client = Substitute.For<IDropboxClient>();
        client.ExistsAsync("/folder/file.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        var provider = BuildProvider(client, RealExecutor());

        var exists = await provider.ExistsAsync("dropbox:///folder/file.txt");

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsyncReturnsFalseWhenClientReportsMissing()
    {
        // The client returns false for a typed path/not_found. The provider
        // passes that value through unchanged. The typed not-found mapping
        // itself is DropboxClientAdapter behavior and is not exercised here.
        var client = Substitute.For<IDropboxClient>();
        client.ExistsAsync("/folder/file.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));
        var provider = BuildProvider(client, RealExecutor());

        var exists = await provider.ExistsAsync("dropbox:///folder/file.txt");

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsyncInvokesDeleteIfExists()
    {
        var client = Substitute.For<IDropboxClient>();
        client.DeleteIfExistsAsync("/folder/file.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        var provider = BuildProvider(client, RealExecutor());

        await provider.DeleteAsync("dropbox:///folder/file.txt");

        await client.Received(1).DeleteIfExistsAsync(
            "/folder/file.txt",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsyncDoesNotThrowWhenMissing()
    {
        var client = Substitute.For<IDropboxClient>();
        client.DeleteIfExistsAsync("/folder/file.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));
        var provider = BuildProvider(client, RealExecutor());

        var act = async () => await provider.DeleteAsync("dropbox:///folder/file.txt");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetMetadataAsyncMapsProperties()
    {
        var modified = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var client = Substitute.For<IDropboxClient>();
        client.GetPropertiesAsync("/folder/file.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Meta(size: 4321, modified: modified)));
        var provider = BuildProvider(client, RealExecutor());

        var meta = await provider.GetMetadataAsync("dropbox:///folder/file.txt");

        meta.SizeBytes.Should().Be(4321);
        meta.LastModifiedUtc.Should().Be(modified);
    }

    [Fact]
    public async Task GetMetadataAsyncMapsExtraFields()
    {
        var client = Substitute.For<IDropboxClient>();
        client.GetPropertiesAsync("/folder/file.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Meta(rev: "r1", hash: "h1", isFolder: false)));
        var provider = BuildProvider(client, RealExecutor());

        var meta = await provider.GetMetadataAsync("dropbox:///folder/file.txt");

        meta.Extra.Should().ContainKey("rev").WhoseValue.Should().Be("r1");
        meta.Extra.Should().ContainKey("content-hash").WhoseValue.Should().Be("h1");
        meta.Extra.Should().ContainKey("is-folder").WhoseValue.Should().Be("false");
    }

    [Fact]
    public async Task GetMetadataAsyncPreservesNullTimestamp()
    {
        var client = Substitute.For<IDropboxClient>();
        client.GetPropertiesAsync("/folder/file.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Meta(modified: null)));
        var provider = BuildProvider(client, RealExecutor());

        var meta = await provider.GetMetadataAsync("dropbox:///folder/file.txt");

        meta.LastModifiedUtc.Should().BeNull();
    }

    [Fact]
    public async Task GetMetadataAsyncUsesRevAsETag()
    {
        var client = Substitute.For<IDropboxClient>();
        client.GetPropertiesAsync("/folder/file.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Meta(rev: "rev-42")));
        var provider = BuildProvider(client, RealExecutor());

        var meta = await provider.GetMetadataAsync("dropbox:///folder/file.txt");

        meta.ETag.Should().Be("rev-42");
    }

    [Fact]
    public async Task CancelledCallThrowsWrappedResilienceException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var client = Substitute.For<IDropboxClient>();
        client.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(x => throw new OperationCanceledException());
        client.GetPropertiesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<DropboxObjectMetadata>>(x => throw new OperationCanceledException());
        var provider = BuildProvider(client, RealExecutor());

        var act = async () => await provider.OpenReadAsync("dropbox:///folder/file.txt", cts.Token);

        await act.Should().ThrowAsync<Portfolio.Resilience.Errors.ResilienceException>();
    }
}
