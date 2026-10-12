// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Providers/GcsObjectProviderTests.cs
// PURPOSE:  Unit tests for GcsObjectProvider. The IGcsClient wrapper is
//           substituted with NSubstitute; no live GCS endpoint is
//           contacted. Verifies URI parsing, per-method resilience policy
//           routing, the write-handle lifecycle, and cancellation.
// RELATION: Exercises FileHarvester.Platform.Cloud.Providers.GcsObjectProvider.
//           Mirrors AzureBlobProviderTests.cs in style; adds policy-
//           routing assertions via a substituted IResilienceExecutor.
// ============================================================================

using System.IO;
using System.Text;
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
/// Tests for <see cref="GcsObjectProvider"/> with a mocked wrapper.
/// </summary>
public sealed class GcsObjectProviderTests
{
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

    private static GcsObjectProvider BuildProvider(IGcsClient client, IResilienceExecutor executor) =>
        new(client, executor);

    // ------------------------------------------------------------------
    // OpenReadAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task OpenReadAsyncParsesBucketAndObject()
    {
        var client = Substitute.For<IGcsClient>();
        client.DownloadAsync("my-bucket", "a.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 })));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var gcs = BuildProvider(client, executor);

        await gcs.OpenReadAsync("gs://my-bucket/a.txt");

        await client.Received(1).DownloadAsync("my-bucket", "a.txt", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenReadAsyncParsesNestedObjectPath()
    {
        var client = Substitute.For<IGcsClient>();
        client.DownloadAsync("my-bucket", "2026/10/a.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream()));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var gcs = BuildProvider(client, executor);

        await gcs.OpenReadAsync("gs://my-bucket/2026/10/a.txt");

        await client.Received(1).DownloadAsync("my-bucket", "2026/10/a.txt", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenReadAsyncRejectsNonGcsScheme()
    {
        var client = Substitute.For<IGcsClient>();
        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var gcs = BuildProvider(client, executor);

        var act = async () => await gcs.OpenReadAsync("s3://bucket/key");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task OpenReadAsyncRejectsGcsWithoutBucket()
    {
        var client = Substitute.For<IGcsClient>();
        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var gcs = BuildProvider(client, executor);

        var act = async () => await gcs.OpenReadAsync("gs://");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task OpenReadAsyncReturnsTheStream()
    {
        var payload = new byte[] { 10, 20, 30 };
        var client = Substitute.For<IGcsClient>();
        client.DownloadAsync("c", "b", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream(payload)));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var gcs = BuildProvider(client, executor);

        await using var stream = await gcs.OpenReadAsync("gs://c/b");
        var buffer = new byte[3];
        var read = await stream.ReadAsync(buffer);

        read.Should().Be(3);
        buffer.Should().Equal(payload);
    }

    // ------------------------------------------------------------------
    // ListAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListAsyncParsesPrefixAndReturnsPage()
    {
        var client = Substitute.For<IGcsClient>();
        client.ListAsync("docs", "2026/", null, 100, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GcsObjectPage(
                new List<string> { "2026/a.txt", "2026/b.txt" },
                "next-token")));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var gcs = BuildProvider(client, executor);

        var page = await gcs.ListAsync("gs://docs/2026/", null, 100);

        page.Entries.Should().HaveCount(2);
        page.Entries[0].Path.Should().Be("gs://docs/2026/a.txt");
        page.Entries[0].Name.Should().Be("a.txt");
        page.Entries[0].IsDirectory.Should().BeFalse();
        page.Entries[0].SizeBytes.Should().Be(0);
        page.ContinuationToken.Should().Be("next-token");
    }

    [Fact]
    public async Task ListAsyncPassesContinuationToken()
    {
        var client = Substitute.For<IGcsClient>();
        client.ListAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GcsObjectPage(Array.Empty<string>(), null)));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var gcs = BuildProvider(client, executor);

        await gcs.ListAsync("gs://docs/", "tok-1", 50);

        await client.Received(1).ListAsync("docs", "", "tok-1", 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsyncRoutesThroughCloudSearch()
    {
        var client = Substitute.For<IGcsClient>();
        client.ListAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GcsObjectPage(Array.Empty<string>(), null)));

        var executor = Substitute.For<IResilienceExecutor>();
        executor.ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<GcsObjectPage>>>(),
            Arg.Any<Func<CancellationToken, Task<GcsObjectPage>>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<Func<CancellationToken, Task<GcsObjectPage>>>(1)(CancellationToken.None));

        var gcs = BuildProvider(client, executor);

        await gcs.ListAsync("gs://docs/", null, 10);

        await executor.Received(1).ExecuteAsync(
            Arg.Is<string>(n => n == PolicyNames.CloudSearch),
            Arg.Any<Func<CancellationToken, Task<GcsObjectPage>>>(),
            Arg.Any<Func<CancellationToken, Task<GcsObjectPage>>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // OpenWriteAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task OpenWriteAsyncReturnsHandleThatCommitsOnComplete()
    {
        var client = Substitute.For<IGcsClient>();
        client.UploadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var gcs = BuildProvider(client, executor);

        var handle = await gcs.OpenWriteAsync("gs://docs/out.txt", "text/plain");
        var bytes = Encoding.UTF8.GetBytes("hello gcs");
        await handle.Stream.WriteAsync(bytes);
        await handle.CompleteAsync();

        await client.Received(1).UploadAsync("docs", "out.txt", "text/plain", Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        await handle.DisposeAsync();
    }

    [Fact]
    public async Task OpenWriteAsyncCompleteRoutesThroughCloudWrite()
    {
        var client = Substitute.For<IGcsClient>();
        client.UploadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
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

        var gcs = BuildProvider(client, executor);
        var handle = await gcs.OpenWriteAsync("gs://docs/out.txt", null);
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
    public async Task OpenWriteAsyncWithoutCompleteDoesNotUpload()
    {
        var client = Substitute.For<IGcsClient>();
        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var gcs = BuildProvider(client, executor);

        var handle = await gcs.OpenWriteAsync("gs://docs/out.txt", null);
        await handle.DisposeAsync();

        await client.DidNotReceive().UploadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // ExistsAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExistsAsyncReturnsTrueWhenObjectExists()
    {
        var client = Substitute.For<IGcsClient>();
        client.ExistsAsync("docs", "a.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var gcs = BuildProvider(client, executor);

        var exists = await gcs.ExistsAsync("gs://docs/a.txt");
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsyncReturnsFalseWhenObjectMissing()
    {
        var client = Substitute.For<IGcsClient>();
        client.ExistsAsync("docs", "missing.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var gcs = BuildProvider(client, executor);

        var exists = await gcs.ExistsAsync("gs://docs/missing.txt");
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsyncRoutesThroughCloudRead()
    {
        var client = Substitute.For<IGcsClient>();
        client.ExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var executor = Substitute.For<IResilienceExecutor>();
        executor.ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<bool>>>(),
            Arg.Any<Func<CancellationToken, Task<bool>>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<Func<CancellationToken, Task<bool>>>(1)(CancellationToken.None));

        var gcs = BuildProvider(client, executor);
        await gcs.ExistsAsync("gs://docs/a.txt");

        await executor.Received(1).ExecuteAsync(
            Arg.Is<string>(n => n == PolicyNames.CloudRead),
            Arg.Any<Func<CancellationToken, Task<bool>>>(),
            Arg.Any<Func<CancellationToken, Task<bool>>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExistsAsyncDoesNotDownloadObjectContent()
    {
        // The existence probe uses only the metadata path. This test
        // asserts the provider never invokes DownloadAsync during
        // ExistsAsync — proving no content is read or buffered.
        var client = Substitute.For<IGcsClient>();
        client.ExistsAsync("docs", "a.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var gcs = BuildProvider(client, executor);

        await gcs.ExistsAsync("gs://docs/a.txt");

        await client.DidNotReceive().DownloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // DeleteAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsyncInvokesDeleteIfExists()
    {
        var client = Substitute.For<IGcsClient>();
        client.DeleteIfExistsAsync("docs", "a.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var gcs = BuildProvider(client, executor);

        await gcs.DeleteAsync("gs://docs/a.txt");

        await client.Received(1).DeleteIfExistsAsync("docs", "a.txt", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsyncDoesNotThrowWhenObjectMissing()
    {
        var client = Substitute.For<IGcsClient>();
        client.DeleteIfExistsAsync("docs", "missing.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var gcs = BuildProvider(client, executor);

        var act = async () => await gcs.DeleteAsync("gs://docs/missing.txt");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteAsyncRoutesThroughCloudWrite()
    {
        var client = Substitute.For<IGcsClient>();
        client.DeleteIfExistsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        var executor = Substitute.For<IResilienceExecutor>();
        executor.ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<bool>>>(),
            Arg.Any<Func<CancellationToken, Task<bool>>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<Func<CancellationToken, Task<bool>>>(1)(CancellationToken.None));

        var gcs = BuildProvider(client, executor);
        await gcs.DeleteAsync("gs://docs/a.txt");

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
        var client = Substitute.For<IGcsClient>();
        client.GetPropertiesAsync("docs", "a.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GcsObjectMetadata(
                SizeBytes: 1234,
                LastModifiedUtc: when,
                ContentType: "text/plain",
                ETag: "abc123",
                Extra: new Dictionary<string, string>())));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var gcs = BuildProvider(client, executor);

        var meta = await gcs.GetMetadataAsync("gs://docs/a.txt");

        meta.SizeBytes.Should().Be(1234);
        meta.ContentType.Should().Be("text/plain");
        meta.ETag.Should().Be("abc123");
        meta.LastModifiedUtc.Should().Be(when);
    }

    [Fact]
    public async Task GetMetadataAsyncMapsExtraFields()
    {
        var extra = new Dictionary<string, string>
        {
            ["storage-class"] = "STANDARD",
            ["generation"] = "1",
            ["md5-hash"] = "placeholder-md5",
        };

        var client = Substitute.For<IGcsClient>();
        client.GetPropertiesAsync("docs", "a.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GcsObjectMetadata(
                SizeBytes: 10,
                LastModifiedUtc: null,
                ContentType: null,
                ETag: null,
                Extra: extra)));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var gcs = BuildProvider(client, executor);

        var meta = await gcs.GetMetadataAsync("gs://docs/a.txt");

        meta.Extra.Should().ContainKey("storage-class");
        meta.Extra["storage-class"].Should().Be("STANDARD");
        meta.Extra["generation"].Should().Be("1");
    }

    [Fact]
    public async Task GetMetadataAsyncRoutesThroughCloudRead()
    {
        var client = Substitute.For<IGcsClient>();
        client.GetPropertiesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GcsObjectMetadata(
                SizeBytes: 0, LastModifiedUtc: null, ContentType: null, ETag: null,
                Extra: new Dictionary<string, string>())));

        var executor = Substitute.For<IResilienceExecutor>();
        executor.ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<GcsObjectMetadata>>>(),
            Arg.Any<Func<CancellationToken, Task<GcsObjectMetadata>>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<Func<CancellationToken, Task<GcsObjectMetadata>>>(1)(CancellationToken.None));

        var gcs = BuildProvider(client, executor);
        await gcs.GetMetadataAsync("gs://docs/a.txt");

        await executor.Received(1).ExecuteAsync(
            Arg.Is<string>(n => n == PolicyNames.CloudRead),
            Arg.Any<Func<CancellationToken, Task<GcsObjectMetadata>>>(),
            Arg.Any<Func<CancellationToken, Task<GcsObjectMetadata>>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // Cancellation
    // ------------------------------------------------------------------

    [Fact]
    public async Task CancelledCallThrowsWrappedResilienceException()
    {
        var client = Substitute.For<IGcsClient>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        client.DownloadAsync("docs", "a.txt", Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(_ => throw new OperationCanceledException());

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var gcs = BuildProvider(client, executor);

        var act = async () => await gcs.OpenReadAsync("gs://docs/a.txt", cts.Token);
        await act.Should().ThrowAsync<Portfolio.Resilience.Errors.ResilienceException>()
            .Where(ex => ex.InnerException is OperationCanceledException);
    }
}
