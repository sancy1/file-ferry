// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Providers/S3CompatibleProviderTests.cs
// PURPOSE:  Unit tests for S3CompatibleProvider. All AWS SDK calls are
//           mocked with NSubstitute. No real AWS endpoint is contacted.
//           Verifies path parsing, resilience routing, and the write
//           handle lifecycle.
// RELATION: Consumes FileHarvester.Platform.Cloud.Providers.S3CompatibleProvider.
// ============================================================================

using System.Net;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using FileHarvester.Core.Composition;
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
/// Tests for <see cref="S3CompatibleProvider"/> with a mocked S3 client.
/// </summary>
public sealed class S3CompatibleProviderTests
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

    private static S3CompatibleProvider BuildProvider(IAmazonS3 client, IResilienceExecutor executor) =>
        new(client, executor);

    // ------------------------------------------------------------------
    // OpenReadAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task OpenReadAsyncParsesBucketAndKey()
    {
        var client = Substitute.For<IAmazonS3>();
        var response = new GetObjectResponse { ResponseStream = new MemoryStream() };

        client.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        using var provider = BuildExecutorProvider();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var s3 = BuildProvider(client, executor);

        await s3.OpenReadAsync("s3://my-bucket/docs/a.txt");

        await client.Received(1).GetObjectAsync(
            Arg.Is<GetObjectRequest>(r => r.BucketName == "my-bucket" && r.Key == "docs/a.txt"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenReadAsyncRejectsNonS3Scheme()
    {
        var client = Substitute.For<IAmazonS3>();
        using var provider = BuildExecutorProvider();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var s3 = BuildProvider(client, executor);

        var act = async () => await s3.OpenReadAsync("gs://bucket/key");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ------------------------------------------------------------------
    // ListAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListAsyncParsesPrefixAndReturnsPage()
    {
        var client = Substitute.For<IAmazonS3>();
        var response = new ListObjectsV2Response
        {
            S3Objects = new List<S3Object>
            {
                new S3Object { Key = "docs/a.txt", Size = 100, LastModified = DateTime.UtcNow },
                new S3Object { Key = "docs/b.txt", Size = 200, LastModified = DateTime.UtcNow },
            },
            NextContinuationToken = "next",
        };

        client.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        using var provider = BuildExecutorProvider();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var s3 = BuildProvider(client, executor);

        var page = await s3.ListAsync("s3://my-bucket/docs/", null, 100);

        page.Entries.Should().HaveCount(2);
        page.Entries[0].Path.Should().Be("s3://my-bucket/docs/a.txt");
        page.Entries[0].SizeBytes.Should().Be(100);
        page.ContinuationToken.Should().Be("next");
    }

    [Fact]
    public async Task ListAsyncPassesContinuationToken()
    {
        var client = Substitute.For<IAmazonS3>();
        client.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ListObjectsV2Response()));

        using var provider = BuildExecutorProvider();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var s3 = BuildProvider(client, executor);

        await s3.ListAsync("s3://my-bucket/docs/", "token-1", 50);

        await client.Received(1).ListObjectsV2Async(
            Arg.Is<ListObjectsV2Request>(r => r.ContinuationToken == "token-1" && r.MaxKeys == 50),
            Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // OpenWriteAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task OpenWriteAsyncReturnsHandleThatCommitsOnComplete()
    {
        var client = Substitute.For<IAmazonS3>();
        client.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PutObjectResponse()));

        using var provider = BuildExecutorProvider();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var s3 = BuildProvider(client, executor);

        var handle = await s3.OpenWriteAsync("s3://my-bucket/out.txt", "text/plain");
        var bytes = Encoding.UTF8.GetBytes("hello cloud");
        await handle.Stream.WriteAsync(bytes);
        await handle.CompleteAsync();

        await client.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(r => r.BucketName == "my-bucket" && r.Key == "out.txt"),
            Arg.Any<CancellationToken>());

        await handle.DisposeAsync();
    }

    // ------------------------------------------------------------------
    // ExistsAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExistsAsyncReturnsTrueWhenMetadataSucceeds()
    {
        var client = Substitute.For<IAmazonS3>();
        var response = new ListObjectsV2Response
        {
            S3Objects = new List<S3Object>
            {
                new S3Object { Key = "a.txt", Size = 100, LastModified = DateTime.UtcNow },
            },
        };

        client.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        using var provider = BuildExecutorProvider();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var s3 = BuildProvider(client, executor);

        var exists = await s3.ExistsAsync("s3://my-bucket/a.txt");
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsyncReturnsFalseWhenNotFound()
    {
        var client = Substitute.For<IAmazonS3>();
        var response = new ListObjectsV2Response
        {
            S3Objects = new List<S3Object>(),
        };

        client.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        using var provider = BuildExecutorProvider();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var s3 = BuildProvider(client, executor);

        var exists = await s3.ExistsAsync("s3://my-bucket/missing.txt");
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsyncReturnsFalseWhenOnlyPrefixMatches()
    {
        // ListObjectsV2 with Prefix returns every key that starts with the
        // prefix. The provider confirms the exact key matches. This test
        // guards that check: an object "a.txt.bak" must not make ExistsAsync
        // return true for the query "a.txt".
        var client = Substitute.For<IAmazonS3>();
        var response = new ListObjectsV2Response
        {
            S3Objects = new List<S3Object>
            {
                new S3Object { Key = "a.txt.bak", Size = 10, LastModified = DateTime.UtcNow },
            },
        };

        client.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        using var provider = BuildExecutorProvider();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var s3 = BuildProvider(client, executor);

        var exists = await s3.ExistsAsync("s3://my-bucket/a.txt");
        exists.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // DeleteAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsyncInvokesSdkDelete()
    {
        var client = Substitute.For<IAmazonS3>();
        client.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DeleteObjectResponse()));

        using var provider = BuildExecutorProvider();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var s3 = BuildProvider(client, executor);

        await s3.DeleteAsync("s3://my-bucket/a.txt");

        await client.Received(1).DeleteObjectAsync(
            Arg.Is<DeleteObjectRequest>(r => r.BucketName == "my-bucket" && r.Key == "a.txt"),
            Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------------
    // GetMetadataAsync
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetMetadataAsyncMapsS3Metadata()
    {
        var client = Substitute.For<IAmazonS3>();
        var response = new GetObjectMetadataResponse
        {
            ETag = "abc123",
            LastModified = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc),
        };
        response.Headers.ContentLength = 1234;
        response.Headers.ContentType = "text/plain";

        client.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        using var provider = BuildExecutorProvider();
        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var s3 = BuildProvider(client, executor);

        var meta = await s3.GetMetadataAsync("s3://my-bucket/a.txt");

        meta.SizeBytes.Should().Be(1234);
        meta.ContentType.Should().Be("text/plain");
        meta.ETag.Should().Be("abc123");
    }
}
