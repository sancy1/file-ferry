// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Providers/AzureBlobProviderTests.cs
// PURPOSE:  Unit tests for AzureBlobProvider. The IAzureBlobClient wrapper
//           is substituted with NSubstitute; no live Azure endpoint is
//           contacted. Verifies URI parsing, per-method resilience policy
//           routing, the write-handle lifecycle, and cancellation.
// RELATION: Exercises FileHarvester.Platform.Cloud.Providers.AzureBlobProvider.
//           Mirrors S3CompatibleProviderTests.cs in style; adds policy-
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
/// Tests for <see cref="AzureBlobProvider"/> with a mocked wrapper.
/// </summary>
public sealed class AzureBlobProviderTests
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

    private static AzureBlobProvider BuildProvider(IAzureBlobClient client, IResilienceExecutor executor) =>
        new(client, executor);

    // ------------------------------------------------------------------
    // OpenReadAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task OpenReadAsyncParsesContainerAndBlob()
    {
        var client = Substitute.For<IAzureBlobClient>();
        client.DownloadAsync("docs", "a.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 })));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var azure = BuildProvider(client, executor);

        await azure.OpenReadAsync("azure://docs/a.txt");

        await client.Received(1).DownloadAsync("docs", "a.txt", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenReadAsyncParsesNestedBlobPath()
    {
        var client = Substitute.For<IAzureBlobClient>();
        client.DownloadAsync("docs", "2026/10/a.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream()));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var azure = BuildProvider(client, executor);

        await azure.OpenReadAsync("azure://docs/2026/10/a.txt");

        await client.Received(1).DownloadAsync("docs", "2026/10/a.txt", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenReadAsyncRejectsNonAzureScheme()
    {
        var client = Substitute.For<IAzureBlobClient>();
        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var azure = BuildProvider(client, executor);

        var act = async () => await azure.OpenReadAsync("s3://bucket/key");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task OpenReadAsyncRejectsAzureWithoutContainer()
    {
        var client = Substitute.For<IAzureBlobClient>();
        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var azure = BuildProvider(client, executor);

        var act = async () => await azure.OpenReadAsync("azure://");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task OpenReadAsyncReturnsTheStream()
    {
        var payload = new byte[] { 10, 20, 30 };
        var client = Substitute.For<IAzureBlobClient>();
        client.DownloadAsync("c", "b", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream(payload)));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var azure = BuildProvider(client, executor);

        await using var stream = await azure.OpenReadAsync("azure://c/b");
        var buffer = new byte[3];
        var read = await stream.ReadAsync(buffer);

        read.Should().Be(3);
        buffer.Should().Equal(payload);
    }

    [Fact]
    public async Task OpenReadAsyncRoutesThroughCloudRead()
    {
        var client = Substitute.For<IAzureBlobClient>();
        client.DownloadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream()));

        var executor = Substitute.For<IResilienceExecutor>();
        executor.ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<Stream>>>(),
            Arg.Any<Func<CancellationToken, Task<Stream>>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<Func<CancellationToken, Task<Stream>>>(1)(CancellationToken.None));

        var azure = BuildProvider(client, executor);

        await azure.OpenReadAsync("azure://c/b");

        await executor.Received(1).ExecuteAsync(
            Arg.Is<string>(n => n == PolicyNames.CloudRead),
            Arg.Any<Func<CancellationToken, Task<Stream>>>(),
            Arg.Any<Func<CancellationToken, Task<Stream>>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // ListAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListAsyncParsesPrefixAndReturnsPage()
    {
        var client = Substitute.For<IAzureBlobClient>();
        client.ListAsync("docs", "2026/", null, 100, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AzureBlobPage(
                new List<string> { "2026/a.txt", "2026/b.txt" },
                "next-token")));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var azure = BuildProvider(client, executor);

        var page = await azure.ListAsync("azure://docs/2026/", null, 100);

        page.Entries.Should().HaveCount(2);
        page.Entries[0].Path.Should().Be("azure://docs/2026/a.txt");
        page.Entries[0].Name.Should().Be("a.txt");
        page.Entries[0].IsDirectory.Should().BeFalse();
        page.Entries[0].SizeBytes.Should().Be(0);
        page.ContinuationToken.Should().Be("next-token");
    }

    [Fact]
    public async Task ListAsyncPassesContinuationToken()
    {
        var client = Substitute.For<IAzureBlobClient>();
        client.ListAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AzureBlobPage(Array.Empty<string>(), null)));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var azure = BuildProvider(client, executor);

        await azure.ListAsync("azure://docs/", "tok-1", 50);

        await client.Received(1).ListAsync("docs", "", "tok-1", 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsyncRoutesThroughCloudSearch()
    {
        var client = Substitute.For<IAzureBlobClient>();
        client.ListAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AzureBlobPage(Array.Empty<string>(), null)));

        var executor = Substitute.For<IResilienceExecutor>();
        executor.ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<AzureBlobPage>>>(),
            Arg.Any<Func<CancellationToken, Task<AzureBlobPage>>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<Func<CancellationToken, Task<AzureBlobPage>>>(1)(CancellationToken.None));

        var azure = BuildProvider(client, executor);

        await azure.ListAsync("azure://docs/", null, 10);

        await executor.Received(1).ExecuteAsync(
            Arg.Is<string>(n => n == PolicyNames.CloudSearch),
            Arg.Any<Func<CancellationToken, Task<AzureBlobPage>>>(),
            Arg.Any<Func<CancellationToken, Task<AzureBlobPage>>>(),
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
        var client = Substitute.For<IAzureBlobClient>();
        client.UploadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var azure = BuildProvider(client, executor);

        var handle = await azure.OpenWriteAsync("azure://docs/out.txt", "text/plain");
        var bytes = Encoding.UTF8.GetBytes("hello azure");
        await handle.Stream.WriteAsync(bytes);
        await handle.CompleteAsync();

        await client.Received(1).UploadAsync("docs", "out.txt", Arg.Any<Stream>(), "text/plain", Arg.Any<CancellationToken>());
        await handle.DisposeAsync();
    }

    [Fact]
    public async Task OpenWriteAsyncCompleteRoutesThroughCloudWrite()
    {
        var client = Substitute.For<IAzureBlobClient>();
        client.UploadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
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

        var azure = BuildProvider(client, executor);
        var handle = await azure.OpenWriteAsync("azure://docs/out.txt", null);
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
        var client = Substitute.For<IAzureBlobClient>();
        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var azure = BuildProvider(client, executor);

        var handle = await azure.OpenWriteAsync("azure://docs/out.txt", null);
        await handle.DisposeAsync();

        await client.DidNotReceive().UploadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // ExistsAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExistsAsyncReturnsTrueWhenBlobExists()
    {
        var client = Substitute.For<IAzureBlobClient>();
        client.ExistsAsync("docs", "a.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var azure = BuildProvider(client, executor);

        var exists = await azure.ExistsAsync("azure://docs/a.txt");
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsyncReturnsFalseWhenBlobMissing()
    {
        var client = Substitute.For<IAzureBlobClient>();
        client.ExistsAsync("docs", "missing.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var azure = BuildProvider(client, executor);

        var exists = await azure.ExistsAsync("azure://docs/missing.txt");
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsyncRoutesThroughCloudRead()
    {
        var client = Substitute.For<IAzureBlobClient>();
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

        var azure = BuildProvider(client, executor);
        await azure.ExistsAsync("azure://docs/a.txt");

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
        var client = Substitute.For<IAzureBlobClient>();
        client.DeleteIfExistsAsync("docs", "a.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var azure = BuildProvider(client, executor);

        await azure.DeleteAsync("azure://docs/a.txt");

        await client.Received(1).DeleteIfExistsAsync("docs", "a.txt", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsyncDoesNotThrowWhenBlobMissing()
    {
        var client = Substitute.For<IAzureBlobClient>();
        client.DeleteIfExistsAsync("docs", "missing.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var azure = BuildProvider(client, executor);

        var act = async () => await azure.DeleteAsync("azure://docs/missing.txt");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteAsyncRoutesThroughCloudWrite()
    {
        var client = Substitute.For<IAzureBlobClient>();
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

        var azure = BuildProvider(client, executor);
        await azure.DeleteAsync("azure://docs/a.txt");

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
    public async Task GetMetadataAsyncMapsBlobProperties()
    {
        var when = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        var client = Substitute.For<IAzureBlobClient>();
        client.GetPropertiesAsync("docs", "a.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AzureBlobMetadata(
                SizeBytes: 1234,
                LastModifiedUtc: when,
                ContentType: "text/plain",
                ETag: "abc123",
                Extra: new Dictionary<string, string>())));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var azure = BuildProvider(client, executor);

        var meta = await azure.GetMetadataAsync("azure://docs/a.txt");

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
            ["blob-type"] = "BlockBlob",
            ["access-tier"] = "Hot",
            ["server-encrypted"] = "True",
        };

        var client = Substitute.For<IAzureBlobClient>();
        client.GetPropertiesAsync("docs", "a.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AzureBlobMetadata(
                SizeBytes: 10,
                LastModifiedUtc: null,
                ContentType: null,
                ETag: null,
                Extra: extra)));

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var azure = BuildProvider(client, executor);

        var meta = await azure.GetMetadataAsync("azure://docs/a.txt");

        meta.Extra.Should().ContainKey("blob-type");
        meta.Extra["blob-type"].Should().Be("BlockBlob");
        meta.Extra["access-tier"].Should().Be("Hot");
    }

    [Fact]
    public async Task GetMetadataAsyncRoutesThroughCloudRead()
    {
        var client = Substitute.For<IAzureBlobClient>();
        client.GetPropertiesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AzureBlobMetadata(
                SizeBytes: 0, LastModifiedUtc: null, ContentType: null, ETag: null,
                Extra: new Dictionary<string, string>())));

        var executor = Substitute.For<IResilienceExecutor>();
        executor.ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<AzureBlobMetadata>>>(),
            Arg.Any<Func<CancellationToken, Task<AzureBlobMetadata>>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<Func<CancellationToken, Task<AzureBlobMetadata>>>(1)(CancellationToken.None));

        var azure = BuildProvider(client, executor);
        await azure.GetMetadataAsync("azure://docs/a.txt");

        await executor.Received(1).ExecuteAsync(
            Arg.Is<string>(n => n == PolicyNames.CloudRead),
            Arg.Any<Func<CancellationToken, Task<AzureBlobMetadata>>>(),
            Arg.Any<Func<CancellationToken, Task<AzureBlobMetadata>>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // Cancellation
    // ------------------------------------------------------------------

    [Fact]
    public async Task CancelledCallThrowsOperationCanceled()
    {
        var client = Substitute.For<IAzureBlobClient>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        client.DownloadAsync("docs", "a.txt", Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(_ => throw new OperationCanceledException());

        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var azure = BuildProvider(client, executor);

        var act = async () => await azure.OpenReadAsync("azure://docs/a.txt", cts.Token);
        await act.Should().ThrowAsync<Portfolio.Resilience.Errors.ResilienceException>()
            .Where(ex => ex.InnerException is OperationCanceledException);
    }

    // ------------------------------------------------------------------
    // ParseAzurePath rejection — one per remaining method.
    // The wrapper and executor are never reached; the provider throws
    // before any call. Mirrors the two OpenRead reject tests above.
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListAsyncRejectsNonAzureScheme()
    {
        var client = Substitute.For<IAzureBlobClient>();
        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var azure = BuildProvider(client, executor);

        var act = async () => await azure.ListAsync("s3://bucket/key", null, 10);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task OpenWriteAsyncRejectsNonAzureScheme()
    {
        var client = Substitute.For<IAzureBlobClient>();
        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var azure = BuildProvider(client, executor);

        var act = async () => await azure.OpenWriteAsync("s3://bucket/key", null);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExistsAsyncRejectsNonAzureScheme()
    {
        var client = Substitute.For<IAzureBlobClient>();
        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var azure = BuildProvider(client, executor);

        var act = async () => await azure.ExistsAsync("s3://bucket/key");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DeleteAsyncRejectsNonAzureScheme()
    {
        var client = Substitute.For<IAzureBlobClient>();
        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var azure = BuildProvider(client, executor);

        var act = async () => await azure.DeleteAsync("s3://bucket/key");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetMetadataAsyncRejectsNonAzureScheme()
    {
        var client = Substitute.For<IAzureBlobClient>();
        using var sp = BuildExecutorProvider();
        var executor = sp.GetRequiredService<IResilienceExecutor>();
        var azure = BuildProvider(client, executor);

        var act = async () => await azure.GetMetadataAsync("s3://bucket/key");
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
