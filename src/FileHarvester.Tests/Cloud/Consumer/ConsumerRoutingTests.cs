// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Consumer/ConsumerRoutingTests.cs
// PURPOSE:  Consumer-driven tests for cloud routing. Each test builds its
//           own registry and substitute provider and drives the cloud
//           resolver exactly as a user path would. No network, no credential,
//           no shared state.
// RELATION: Exercises PathScheme parsing, CloudProviderRegistry resolution,
//           and CloudFileResolver dispatch through StubCloudProvider.
// NOTES
//   Group 1 of the consumer-driven verification. Each test is fully
//   isolated: its own ServiceProvider, registry, and substitute. No static
//   or shared mutable state. No production type is modified.
// ============================================================================

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Registry;
using FileHarvester.Platform.Cloud.Services;
using FileHarvester.Tests.Cloud.Services;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud.Consumer;

/// <summary>
/// Consumer-driven routing tests for the cloud surface.
/// </summary>
public sealed class ConsumerRoutingTests
{
    private static ProviderProfile Profile(string id)
        => new(
            Id: id,
            DisplayName: id,
            Kind: CloudProviderKind.S3Compatible,
            AuthKind: AuthKind.LongLivedKey,
            CredentialFields: Array.Empty<FieldSpec>(),
            DefaultEndpointUrl: null,
            HelpPromptTemplate: "prompt",
            IconKey: null);

    /// <summary>
    /// Builds an isolated CloudFileResolver whose registry resolves S3 to
    /// the given substitute. The factory records the instance it received.
    /// </summary>
    private static CloudFileResolver BuildResolver(StubCloudProvider substitute)
    {
        var registry = new CloudProviderRegistry();
        registry.Register(
            Profile("aws-s3"),
            (instance, _) =>
            {
                substitute.FactoryInstance = instance;
                substitute.FactoryScheme = PathSchemeKind.S3;
                return Task.FromResult<ICloudProvider>(substitute);
            });

        return new CloudFileResolver(
            (scheme, instance, ct) => registry.ResolveAsync(scheme, instance, ct));
    }

    [Fact]
    public async Task BarePathWithOneInstanceReachesTheSubstitute()
    {
        var substitute = new StubCloudProvider();
        substitute.OnExists = _ => Task.FromResult(false);
        var resolver = BuildResolver(substitute);

        var requests = new[] { new FileRequest(Index: 0, RawInput: "s3://example-bucket/reports/file.txt") };
        await resolver.ResolveAsync(requests, progress: null, CancellationToken.None);

        substitute.FactoryScheme.Should().Be(PathSchemeKind.S3);
        substitute.FactoryInstance.Should().BeNull();
        substitute.Calls.Should().ContainSingle();
        substitute.Calls[0].Should().Be("ExistsAsync:s3://example-bucket/reports/file.txt");
    }

    [Fact]
    public async Task ExplicitInstanceReachesTheSubstitute()
    {
        var substitute = new StubCloudProvider();
        substitute.OnExists = _ => Task.FromResult(false);
        var resolver = BuildResolver(substitute);

        var requests = new[] { new FileRequest(Index: 0, RawInput: "s3://[work]/example-bucket/reports/file.txt") };
        await resolver.ResolveAsync(requests, progress: null, CancellationToken.None);

        substitute.FactoryScheme.Should().Be(PathSchemeKind.S3);
        substitute.FactoryInstance.Should().Be("work");
        substitute.Calls.Should().ContainSingle();
        substitute.Calls[0].Should().Be("ExistsAsync:s3://[work]/example-bucket/reports/file.txt");
    }

    [Fact]
    public async Task ReadReturnsContentToTheConsumer()
    {
        var substitute = new StubCloudProvider();
        substitute.OnExists = _ => Task.FromResult(true);
        substitute.OnGetMetadata = _ => Task.FromResult(new ProviderMetadata(
            SizeBytes: 5,
            LastModifiedUtc: DateTime.UtcNow,
            ContentType: "text/plain",
            ETag: null,
            Extra: new System.Collections.Generic.Dictionary<string, string>()));
        var resolver = BuildResolver(substitute);

        var requests = new[] { new FileRequest(Index: 0, RawInput: "s3://example-bucket/reports/file.txt") };
        var resolved = await resolver.ResolveAsync(requests, progress: null, CancellationToken.None);

        resolved.Should().ContainSingle();
        resolved[0].AbsolutePath.Should().Be("s3://example-bucket/reports/file.txt");
        substitute.Calls.Should().HaveCount(2);
        substitute.Calls[0].Should().StartWith("ExistsAsync:");
        substitute.Calls[1].Should().StartWith("GetMetadataAsync:");
    }

    [Fact]
    public async Task ReadCancellationPropagates()
    {
        var substitute = new StubCloudProvider();
        substitute.OnExists = _ => throw new OperationCanceledException();
        var resolver = BuildResolver(substitute);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var requests = new[] { new FileRequest(Index: 0, RawInput: "s3://example-bucket/reports/file.txt") };
        var act = async () => await resolver.ResolveAsync(requests, progress: null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ProviderFailurePropagatesFromTheResolver()
    {
        var substitute = new StubCloudProvider();
        substitute.OnExists = _ => throw new IOException("simulated failure");
        var resolver = BuildResolver(substitute);

        var requests = new[] { new FileRequest(Index: 0, RawInput: "s3://example-bucket/reports/file.txt") };
        var act = async () => await resolver.ResolveAsync(requests, progress: null, CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
        substitute.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task ResolveOfOutputPathReachesTheProvider()
    {
        var substitute = new StubCloudProvider();
        substitute.OnExists = _ => Task.FromResult(false);
        substitute.OnOpenWrite = (path, _) =>
        {
            substitute.LastWriteContent = path;
            return Task.FromResult(new CloudWriteHandle(
                new MemoryStream(),
                _ => Task.CompletedTask));
        };
        var resolver = BuildResolver(substitute);

        var requests = new[] { new FileRequest(Index: 0, RawInput: "s3://example-bucket/reports/output.txt") };
        await resolver.ResolveAsync(requests, progress: null, CancellationToken.None);

        substitute.Calls.Should().ContainSingle();
        substitute.Calls[0].Should().Be("ExistsAsync:s3://example-bucket/reports/output.txt");
    }

    [Fact]
    public async Task ResolveFailurePropagatesForOutputPath()
    {
        var substitute = new StubCloudProvider();
        substitute.OnExists = _ => throw new IOException("simulated write-path failure");
        var resolver = BuildResolver(substitute);

        var requests = new[] { new FileRequest(Index: 0, RawInput: "s3://example-bucket/reports/output.txt") };
        var act = async () => await resolver.ResolveAsync(requests, progress: null, CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
        substitute.Calls.Should().ContainSingle();
    }
}
