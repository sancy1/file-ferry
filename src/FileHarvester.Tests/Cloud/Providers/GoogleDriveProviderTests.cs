// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Providers/GoogleDriveProviderTests.cs
// PURPOSE:  Unit tests for GoogleDriveProvider. The IGDriveClient wrapper is
//           substituted with NSubstitute; no live Google endpoint is
//           contacted. Verifies gdrive:// parsing, the metadata-first
//           OpenReadAsync including the Google-native export throw, listing
//           and continuation, the update-not-create write path, existence,
//           deletion, metadata mapping, policy routing, and cancellation.
// RELATION: Exercises FileHarvester.Platform.Cloud.Providers.GoogleDriveProvider.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Core.Composition;
using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Providers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Extensions;
using Portfolio.Resilience.Sinks;
using Xunit;

namespace FileHarvester.Tests.Cloud.Providers;

/// <summary>
/// Tests for <see cref="GoogleDriveProvider"/> with a mocked wrapper.
/// </summary>
public sealed class GoogleDriveProviderTests
{
    private const string FileId = "the-file-id";
    private const string FolderId = "the-folder-id";

    private static ServiceProvider BuildExecutorProvider()
    {
        var services = new ServiceCollection();
        services.AddPortfolioResilience(r =>
        {
            r.AddLogSink(new ConsoleLogSink());

            r.AddPolicy(PolicyNames.CloudRead, p =>
            {
                p.Retry.MaxAttempts = 1;
                p.Retry.BaseDelayMs = 10;
                p.Circuit.FailureThreshold = 100;
                p.Circuit.OpenDurationSeconds = 30;
                p.Timeout.TimeoutMs = 30_000;
            });

            r.AddPolicy(PolicyNames.CloudWrite, p =>
            {
                p.Retry.MaxAttempts = 1;
                p.Retry.BaseDelayMs = 10;
                p.Circuit.FailureThreshold = 100;
                p.Circuit.OpenDurationSeconds = 30;
                p.Timeout.TimeoutMs = 60_000;
            });

            r.AddPolicy(PolicyNames.CloudSearch, p =>
            {
                p.Retry.MaxAttempts = 1;
                p.Retry.BaseDelayMs = 10;
                p.Circuit.FailureThreshold = 100;
                p.Circuit.OpenDurationSeconds = 30;
                p.Timeout.TimeoutMs = 60_000;
            });
        });

        return services.BuildServiceProvider();
    }

    private static GoogleDriveProvider BuildProvider(IGDriveClient client, IResilienceExecutor executor)
        => new(client, executor);

    private static GDriveObjectMetadata Meta(
        string id,
        string? mime = "text/plain",
        long size = 10,
        DateTime? modified = null,
        string? md5 = null,
        bool trashed = false)
        => new(id, "name", mime, size, modified, md5, trashed);

    // ------------------------------------------------------------------
    // OpenReadAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task OpenReadAsyncParsesFileId()
    {
        var client = Substitute.For<IGDriveClient>();
        client.GetPropertiesAsync(FileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Meta(FileId)));
        client.DownloadAsync(FileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 })));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var provider = BuildProvider(client, executor);

        await using var stream = await provider.OpenReadAsync("gdrive://" + FileId);

        await client.Received(1).DownloadAsync(FileId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenReadAsyncStripsTheInstanceSegment()
    {
        var client = Substitute.For<IGDriveClient>();
        client.GetPropertiesAsync(FileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Meta(FileId)));
        client.DownloadAsync(FileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 })));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var provider = BuildProvider(client, executor);

        await using var stream = await provider.OpenReadAsync("gdrive://[work]/" + FileId);

        await client.Received(1).DownloadAsync(FileId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenReadAsyncRejectsNonGDriveScheme()
    {
        var client = Substitute.For<IGDriveClient>();
        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var provider = BuildProvider(client, executor);

        var act = async () => await provider.OpenReadAsync("s3://bucket/key");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task OpenReadAsyncRejectsGDriveWithNoId()
    {
        var client = Substitute.For<IGDriveClient>();
        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var provider = BuildProvider(client, executor);

        var act = async () => await provider.OpenReadAsync("gdrive://");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task OpenReadAsyncThrowsForGoogleNativeMime()
    {
        var client = Substitute.For<IGDriveClient>();
        client.GetPropertiesAsync(FileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Meta(FileId, mime: "application/vnd.google-apps.document")));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var provider = BuildProvider(client, executor);

        var act = async () => await provider.OpenReadAsync("gdrive://" + FileId);
        await act.Should().ThrowAsync<GoogleNativeFileRequiresExportException>();

        await client.DidNotReceive().DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenReadAsyncReturnsTheStream()
    {
        var payload = new byte[] { 10, 20, 30 };
        var client = Substitute.For<IGDriveClient>();
        client.GetPropertiesAsync(FileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Meta(FileId)));
        client.DownloadAsync(FileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream(payload)));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var provider = BuildProvider(client, executor);

        await using var stream = await provider.OpenReadAsync("gdrive://" + FileId);
        var buffer = new byte[3];
        var read = await stream.ReadAsync(buffer);

        read.Should().Be(3);
        buffer.Should().Equal(payload);
    }

    // ------------------------------------------------------------------
    // ListAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListAsyncParsesFolderIdAndReturnsPage()
    {
        var client = Substitute.For<IGDriveClient>();
        client.ListAsync(FolderId, null, 100, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GDriveObjectPage(
                new List<GDriveObjectMetadata> { Meta("a"), Meta("b") },
                "next-token")));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var provider = BuildProvider(client, executor);

        var page = await provider.ListAsync("gdrive://" + FolderId, null, 100);

        page.Entries.Should().HaveCount(2);
        page.Entries[0].Path.Should().Be("gdrive://a");
        page.ContinuationToken.Should().Be("next-token");
    }

    [Fact]
    public async Task ListAsyncPassesContinuationToken()
    {
        var client = Substitute.For<IGDriveClient>();
        client.ListAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GDriveObjectPage(Array.Empty<GDriveObjectMetadata>(), null)));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var provider = BuildProvider(client, executor);

        await provider.ListAsync("gdrive://" + FolderId, "tok-1", 50);

        await client.Received(1).ListAsync(FolderId, "tok-1", 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsyncMarksFoldersAsDirectories()
    {
        var client = Substitute.For<IGDriveClient>();
        client.ListAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GDriveObjectPage(
                new List<GDriveObjectMetadata>
                {
                    Meta("f1", mime: "application/vnd.google-apps.folder"),
                },
                null)));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var provider = BuildProvider(client, executor);

        var page = await provider.ListAsync("gdrive://" + FolderId, null, 10);

        page.Entries[0].IsDirectory.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // OpenWriteAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task OpenWriteAsyncCompleteCallsUpdateWithExactIdAndNeverCreate()
    {
        var client = Substitute.For<IGDriveClient>();
        client.UpdateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var provider = BuildProvider(client, executor);

        var handle = await provider.OpenWriteAsync("gdrive://" + FileId, "text/plain");
        var bytes = Encoding.UTF8.GetBytes("hello drive");
        await handle.Stream.WriteAsync(bytes);
        await handle.CompleteAsync();

        // The write targets the exact existing file id via UpdateAsync.
        await client.Received(1).UpdateAsync(FileId, "text/plain", Arg.Any<Stream>(), Arg.Any<CancellationToken>());

        // It never creates a new file named after the id.
        await client.DidNotReceive().UploadAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());

        await handle.DisposeAsync();
    }

    [Fact]
    public async Task OpenWriteAsyncCompleteRoutesThroughCloudWrite()
    {
        var client = Substitute.For<IGDriveClient>();
        client.UpdateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var executor = Substitute.For<IResilienceExecutor>();
        executor.ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<Func<CancellationToken, Task>>(1)(CancellationToken.None));

        var provider = BuildProvider(client, executor);
        var handle = await provider.OpenWriteAsync("gdrive://" + FileId, null);
        await handle.CompleteAsync();

        await executor.Received(1).ExecuteAsync(
            Arg.Is<string>(n => n == PolicyNames.CloudWrite),
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<Func<CancellationToken, Task>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>());
        await handle.DisposeAsync();
    }

    [Fact]
    public async Task OpenWriteAsyncWithoutCompleteDoesNotWrite()
    {
        var client = Substitute.For<IGDriveClient>();
        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var provider = BuildProvider(client, executor);

        var handle = await provider.OpenWriteAsync("gdrive://" + FileId, null);
        await handle.DisposeAsync();

        await client.DidNotReceive().UpdateAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        await client.DidNotReceive().UploadAsync(
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // ExistsAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExistsAsyncReturnsTrueWhenPresent()
    {
        var client = Substitute.For<IGDriveClient>();
        client.ExistsAsync(FileId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var provider = BuildProvider(client, executor);

        var exists = await provider.ExistsAsync("gdrive://" + FileId);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsyncReturnsFalseWhenMissing()
    {
        var client = Substitute.For<IGDriveClient>();
        client.ExistsAsync(FileId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var provider = BuildProvider(client, executor);

        var exists = await provider.ExistsAsync("gdrive://" + FileId);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsyncRoutesThroughCloudRead()
    {
        var client = Substitute.For<IGDriveClient>();
        client.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

        var executor = Substitute.For<IResilienceExecutor>();
        executor.ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<bool>>>(),
            Arg.Any<Func<CancellationToken, Task<bool>>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<Func<CancellationToken, Task<bool>>>(1)(CancellationToken.None));

        var provider = BuildProvider(client, executor);
        await provider.ExistsAsync("gdrive://" + FileId);

        await executor.Received(1).ExecuteAsync(
            Arg.Is<string>(n => n == PolicyNames.CloudRead),
            Arg.Any<Func<CancellationToken, Task<bool>>>(),
            Arg.Any<Func<CancellationToken, Task<bool>>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // DeleteAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsyncInvokesDeleteIfExists()
    {
        var client = Substitute.For<IGDriveClient>();
        client.DeleteIfExistsAsync(FileId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var provider = BuildProvider(client, executor);

        await provider.DeleteAsync("gdrive://" + FileId);

        await client.Received(1).DeleteIfExistsAsync(FileId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsyncDoesNotThrowWhenMissing()
    {
        var client = Substitute.For<IGDriveClient>();
        client.DeleteIfExistsAsync(FileId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var provider = BuildProvider(client, executor);

        var act = async () => await provider.DeleteAsync("gdrive://" + FileId);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteAsyncRoutesThroughCloudWrite()
    {
        var client = Substitute.For<IGDriveClient>();
        client.DeleteIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));

        var executor = Substitute.For<IResilienceExecutor>();
        executor.ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<bool>>>(),
            Arg.Any<Func<CancellationToken, Task<bool>>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<Func<CancellationToken, Task<bool>>>(1)(CancellationToken.None));

        var provider = BuildProvider(client, executor);
        await provider.DeleteAsync("gdrive://" + FileId);

        await executor.Received(1).ExecuteAsync(
            Arg.Is<string>(n => n == PolicyNames.CloudWrite),
            Arg.Any<Func<CancellationToken, Task<bool>>>(),
            Arg.Any<Func<CancellationToken, Task<bool>>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // GetMetadataAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetMetadataAsyncMapsProperties()
    {
        var when = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        var client = Substitute.For<IGDriveClient>();
        client.GetPropertiesAsync(FileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GDriveObjectMetadata(
                Id: FileId,
                Name: "name",
                MimeType: "text/plain",
                SizeBytes: 1234,
                LastModifiedUtc: when,
                Md5Checksum: "abc",
                IsTrashed: false)));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var provider = BuildProvider(client, executor);

        var meta = await provider.GetMetadataAsync("gdrive://" + FileId);

        meta.SizeBytes.Should().Be(1234);
        meta.ContentType.Should().Be("text/plain");
        meta.LastModifiedUtc.Should().Be(when);
    }

    [Fact]
    public async Task GetMetadataAsyncMapsExtraFields()
    {
        var client = Substitute.For<IGDriveClient>();
        client.GetPropertiesAsync(FileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GDriveObjectMetadata(
                Id: FileId,
                Name: "name",
                MimeType: "text/plain",
                SizeBytes: 10,
                LastModifiedUtc: null,
                Md5Checksum: "md5-value",
                IsTrashed: false)));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var provider = BuildProvider(client, executor);

        var meta = await provider.GetMetadataAsync("gdrive://" + FileId);

        meta.Extra.Should().ContainKey("md5-checksum");
        meta.Extra["md5-checksum"].Should().Be("md5-value");
    }

    [Fact]
    public async Task GetMetadataAsyncPreservesNullTimestamp()
    {
        var client = Substitute.For<IGDriveClient>();
        client.GetPropertiesAsync(FileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GDriveObjectMetadata(
                Id: FileId,
                Name: "name",
                MimeType: "text/plain",
                SizeBytes: 10,
                LastModifiedUtc: null,
                Md5Checksum: null,
                IsTrashed: false)));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var provider = BuildProvider(client, executor);

        var meta = await provider.GetMetadataAsync("gdrive://" + FileId);

        meta.LastModifiedUtc.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // Cancellation
    // ------------------------------------------------------------------

    [Fact]
    public async Task CancelledCallThrowsWrappedResilienceException()
    {
        var client = Substitute.For<IGDriveClient>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        client.DownloadAsync(FileId, Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(_ => throw new OperationCanceledException());
        client.GetPropertiesAsync(FileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Meta(FileId)));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var provider = BuildProvider(client, executor);

        var act = async () => await provider.OpenReadAsync("gdrive://" + FileId, cts.Token);
        await act.Should().ThrowAsync<Portfolio.Resilience.Errors.ResilienceException>()
            .Where(ex => ex.InnerException is OperationCanceledException);
    }
}
