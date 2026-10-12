// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Services/CloudContentReaderTests.cs
// PURPOSE:  Unit tests for CloudContentReader. Uses StubCloudProvider.
//           Verifies encoding detection, binary detection, SHA-256,
//           the unreadable-placeholder contract, and non-fail-fast
//           batch semantics.
// RELATION: Consumes CloudContentReader and StubCloudProvider. No I/O,
//           no network, no resilience.
// ============================================================================

using System.Text;
using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Services;
using FileHarvester.Tests.Infrastructure;
using FluentAssertions;
using Portfolio.Resilience.Errors;
using Xunit;
using FileHarvester.Core.Composition;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.Tests.Cloud.Services;

/// <summary>
/// Tests for <see cref="CloudContentReader"/>.
/// </summary>
public sealed class CloudContentReaderTests
{
    private static ResolvedFile MakeResolved(string cloudPath, int index = 0)
    {
        var request = new FileRequest(Index: index, RawInput: cloudPath);
        return new ResolvedFile(
            Request: request,
            AbsolutePath: cloudPath,
            SizeBytes: 0,
            LastModifiedUtc: DateTime.UtcNow,
            Confidence: ResolutionConfidence.Searched);
    }

    private static (CloudContentReader Reader, StubCloudProvider Provider) Build(
        PathSchemeKind kind = PathSchemeKind.S3)
    {
        var provider = new StubCloudProvider();
        var reader = new CloudContentReader((k, _, _) => Task.FromResult(k == kind ? CloudProviderResolution.Resolved(provider) : CloudProviderResolution.Failed(CloudResolutionFailure.NoProviderRegistered)));
        return (reader, provider);
    }

    [Fact]
    public async Task ReadAsyncReturnsFileContentForReadableFile()
    {
        var (reader, provider) = Build();
        var payload = Encoding.UTF8.GetBytes("hello cloud");
        provider.OnOpenRead = _ => Task.FromResult<Stream>(new MemoryStream(payload));

        var content = await reader.ReadAsync(MakeResolved("s3://bucket/a.txt"));

        content.IsUnreadable.Should().BeFalse();
        content.IsBinary.Should().BeFalse();
        content.DecodedText.Should().Be("hello cloud");
        content.RawBytes.Should().Equal(payload);
        content.Sha256.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ReadAsyncDetectsUtf8Encoding()
    {
        var (reader, provider) = Build();
        var text = "Café • Überall • 日本語";
        var payload = Encoding.UTF8.GetBytes(text);
        provider.OnOpenRead = _ => Task.FromResult<Stream>(new MemoryStream(payload));

        var content = await reader.ReadAsync(MakeResolved("s3://bucket/unicode.txt"));

        content.DecodedText.Should().Be(text);
        content.IsBinary.Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsyncDetectsBinaryContent()
    {
        var (reader, provider) = Build();
        var payload = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x0D, 0x0A, 0x1A };
        provider.OnOpenRead = _ => Task.FromResult<Stream>(new MemoryStream(payload));

        var content = await reader.ReadAsync(MakeResolved("s3://bucket/image.png"));

        content.IsBinary.Should().BeTrue();
        content.DecodedText.Should().BeEmpty();
        content.RawBytes.Should().Equal(payload);
    }

    [Fact]
    public async Task ReadAsyncReturnsUnreadableOnProviderException()
    {
        using var provider = TestHarness.CreateProviderWithPolicy(
            PolicyNames.CloudRead,
            failureThreshold: 100,
            maxAttempts: 1);
        var executor = provider.GetRequiredService<IResilienceExecutor>();

        var (reader, stub) = Build();
        stub.OnOpenRead = async _ =>
        {
            await executor.ExecuteAsync(
                policyName: PolicyNames.CloudRead,
                operation: _ => throw new IOException("simulated provider failure"),
                ct: CancellationToken.None);
            throw new InvalidOperationException("unreachable");
        };

        var content = await reader.ReadAsync(MakeResolved("s3://bucket/missing.txt"));

        content.IsUnreadable.Should().BeTrue();
        content.ReadError.Should().NotBeNullOrWhiteSpace();
        content.RawBytes.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadManyAsyncIsNonFailFast()
    {
        using var provider = TestHarness.CreateProviderWithPolicy(
            PolicyNames.CloudRead,
            failureThreshold: 100,
            maxAttempts: 1);
        var executor = provider.GetRequiredService<IResilienceExecutor>();

        var (reader, stub) = Build();
        var callCount = 0;
        stub.OnOpenRead = async path =>
        {
            callCount++;
            if (path.Contains("bad", StringComparison.Ordinal))
            {
                await executor.ExecuteAsync(
                    policyName: PolicyNames.CloudRead,
                    operation: _ => throw new IOException("simulated provider failure"),
                    ct: CancellationToken.None);
                throw new InvalidOperationException("unreachable");
            }
            return new MemoryStream(Encoding.UTF8.GetBytes("ok"));
        };

        var files = new[]
        {
            MakeResolved("s3://bucket/a.txt", 0),
            MakeResolved("s3://bucket/bad.txt", 1),
            MakeResolved("s3://bucket/c.txt", 2),
        };

        var ticks = new List<ReadProgress>();
        var progress = new SyncProgress<ReadProgress>(p => ticks.Add(p));

        var results = await reader.ReadManyAsync(files, progress);

        results.Should().HaveCount(3);
        results[0].IsUnreadable.Should().BeFalse();
        results[1].IsUnreadable.Should().BeTrue();
        results[2].IsUnreadable.Should().BeFalse();

        callCount.Should().Be(3);
        ticks.Should().HaveCount(3);
        ticks[1].Status.Should().Be(FileStatus.ReadError);
    }

    [Fact]
    public async Task ReadManyAsyncCancellationPropagates()
    {
        var (reader, provider) = Build();
        provider.OnOpenRead = _ => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("x")));

        var files = new[] { MakeResolved("s3://bucket/a.txt", 0) };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await reader.ReadManyAsync(files, null, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
