// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Providers/OneDriveProviderTests.cs
// PURPOSE:  Consumer-driven tests for OneDriveProvider. Uses a substitute
//           IOneDriveClient and a real IResilienceExecutor. No Graph client,
//           no Microsoft account, no browser, no network call.
// RELATION: Exercises OneDriveProvider only. MicrosoftGraphOneDriveClient
//           has no substitute seam and its direct tests are F-140.
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

/// <summary>Tests for OneDriveProvider.</summary>
public sealed class OneDriveProviderTests
{
    private const string ItemId = "test-item-id";

    private static OneDriveObjectMetadata Meta(
        string id = ItemId,
        string? mime = "text/plain",
        long size = 1234,
        DateTime? modified = null,
        string? etag = "etag-value",
        bool isFolder = false)
        => new(id, "name", mime, size, modified, etag, isFolder);

    private static OneDriveProvider BuildProvider(
        IOneDriveClient client,
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
    public async Task OpenReadAsyncReturnsTheStream()
    {
        var payload = new byte[] { 1, 2, 3 };
        var client = Substitute.For<IOneDriveClient>();
        client.DownloadAsync(ItemId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Stream>(new MemoryStream(payload)));
        var provider = BuildProvider(client, RealExecutor());

        await using var stream = await provider.OpenReadAsync("onedrive://" + ItemId);
        var buffer = new byte[3];
        var read = await stream.ReadAsync(buffer.AsMemory(), CancellationToken.None);

        read.Should().Be(3);
        buffer.Should().Equal(payload);
    }

    [Fact]
    public async Task ListAsyncParsesFolderIdAndReturnsPage()
    {
        var folderId = "test-folder-id";
        var client = Substitute.For<IOneDriveClient>();
        client.ListAsync(folderId, null, 100, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OneDriveObjectPage(
                new[] { Meta("a"), Meta("b") }, null)));
        var provider = BuildProvider(client, RealExecutor());

        var page = await provider.ListAsync("onedrive://" + folderId, null, 100);

        page.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListAsyncMarksFoldersAsDirectories()
    {
        var folderId = "test-folder-id";
        var client = Substitute.For<IOneDriveClient>();
        client.ListAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OneDriveObjectPage(
                new[] { Meta("a", isFolder: true) }, null)));
        var provider = BuildProvider(client, RealExecutor());

        var page = await provider.ListAsync("onedrive://" + folderId, null, 10);

        page.Entries[0].IsDirectory.Should().BeTrue();
    }

    [Fact]
    public async Task OpenReadAsyncParsesItemId()
    {
        var client = Substitute.For<IOneDriveClient>();
        client.GetPropertiesAsync(ItemId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Meta()));
        client.DownloadAsync(ItemId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Stream>(new MemoryStream()));
        var provider = BuildProvider(client, RealExecutor());

        await using var stream = await provider.OpenReadAsync("onedrive://" + ItemId);

        await client.Received(1).DownloadAsync(ItemId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenReadAsyncStripsTheInstanceSegment()
    {
        var client = Substitute.For<IOneDriveClient>();
        client.GetPropertiesAsync(ItemId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Meta()));
        client.DownloadAsync(ItemId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Stream>(new MemoryStream()));
        var provider = BuildProvider(client, RealExecutor());

        await using var stream = await provider.OpenReadAsync("onedrive://[work]/" + ItemId);

        await client.Received(1).DownloadAsync(ItemId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenReadAsyncRejectsNonOneDriveScheme()
    {
        var client = Substitute.For<IOneDriveClient>();
        var provider = BuildProvider(client, RealExecutor());

        var act = async () => await provider.OpenReadAsync("s3://bucket/key");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task OpenReadAsyncRejectsOneDriveWithNoId()
    {
        var client = Substitute.For<IOneDriveClient>();
        var provider = BuildProvider(client, RealExecutor());

        var act = async () => await provider.OpenReadAsync("onedrive://");

        await act.Should().ThrowAsync<ArgumentException>();
    }


    [Fact]
    public async Task OpenWriteAsyncCompleteCallsUpdateWithExactId()
    {
        var client = Substitute.For<IOneDriveClient>();
        client.UpdateAsync(ItemId, Arg.Any<string?>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var provider = BuildProvider(client, RealExecutor());

        var handle = await provider.OpenWriteAsync("onedrive://" + ItemId, "text/plain");
        handle.Stream.WriteByte(1);
        await handle.CompleteAsync();

        await client.Received(1).UpdateAsync(ItemId, "text/plain", Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenWriteAsyncCompleteRoutesThroughCloudWrite()
    {
        var client = Substitute.For<IOneDriveClient>();
        var executor = Substitute.For<IResilienceExecutor>();
        executor.ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var provider = BuildProvider(client, executor);

        var handle = await provider.OpenWriteAsync("onedrive://" + ItemId, null);
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
    public async Task OpenWriteAsyncWithoutCompleteDoesNotWrite()
    {
        var client = Substitute.For<IOneDriveClient>();
        var provider = BuildProvider(client, RealExecutor());

        await using var handle = await provider.OpenWriteAsync("onedrive://" + ItemId, null);
        handle.Stream.WriteByte(1);

        await client.DidNotReceive().UpdateAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExistsAsyncReturnsTrueWhenPresent()
    {
        var client = Substitute.For<IOneDriveClient>();
        client.ExistsAsync(ItemId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
        var provider = BuildProvider(client, RealExecutor());

        var exists = await provider.ExistsAsync("onedrive://" + ItemId);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsyncReturnsFalseWhenMissing()
    {
        var client = Substitute.For<IOneDriveClient>();
        client.ExistsAsync(ItemId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));
        var provider = BuildProvider(client, RealExecutor());

        var exists = await provider.ExistsAsync("onedrive://" + ItemId);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsyncInvokesDeleteIfExists()
    {
        var client = Substitute.For<IOneDriveClient>();
        client.DeleteIfExistsAsync(ItemId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
        var provider = BuildProvider(client, RealExecutor());

        await provider.DeleteAsync("onedrive://" + ItemId);

        await client.Received(1).DeleteIfExistsAsync(ItemId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsyncDoesNotThrowWhenMissing()
    {
        var client = Substitute.For<IOneDriveClient>();
        client.DeleteIfExistsAsync(ItemId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));
        var provider = BuildProvider(client, RealExecutor());

        var act = async () => await provider.DeleteAsync("onedrive://" + ItemId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetMetadataAsyncMapsProperties()
    {
        var when = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var client = Substitute.For<IOneDriveClient>();
        client.GetPropertiesAsync(ItemId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Meta(mime: "text/plain", size: 1234, modified: when)));
        var provider = BuildProvider(client, RealExecutor());

        var meta = await provider.GetMetadataAsync("onedrive://" + ItemId);

        meta.SizeBytes.Should().Be(1234);
        meta.ContentType.Should().Be("text/plain");
        meta.LastModifiedUtc.Should().Be(when);
    }

    [Fact]
    public async Task GetMetadataAsyncMapsExtraFields()
    {
        var client = Substitute.For<IOneDriveClient>();
        client.GetPropertiesAsync(ItemId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Meta(etag: "etag-value")));
        var provider = BuildProvider(client, RealExecutor());

        var meta = await provider.GetMetadataAsync("onedrive://" + ItemId);

        meta.Extra.Should().ContainKey("etag");
        meta.Extra["etag"].Should().Be("etag-value");
    }

    [Fact]
    public async Task GetMetadataAsyncPreservesNullTimestamp()
    {
        var client = Substitute.For<IOneDriveClient>();
        client.GetPropertiesAsync(ItemId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Meta(modified: null)));
        var provider = BuildProvider(client, RealExecutor());

        var meta = await provider.GetMetadataAsync("onedrive://" + ItemId);

        meta.LastModifiedUtc.Should().BeNull();
    }

    [Fact]
    public async Task CancelledCallThrowsWrappedResilienceException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var client = Substitute.For<IOneDriveClient>();
        client.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(x => throw new OperationCanceledException());
        client.GetPropertiesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<OneDriveObjectMetadata>>(x => throw new OperationCanceledException());
        var provider = BuildProvider(client, RealExecutor());

        var act = async () => await provider.OpenReadAsync("onedrive://" + ItemId, cts.Token);

        await act.Should().ThrowAsync<Portfolio.Resilience.Errors.ResilienceException>();
    }
}
