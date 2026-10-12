// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Providers/GraphDriveIdResolverTests.cs
// PURPOSE:  Unit tests for GraphDriveIdResolver. Substitutes the narrow
//           IDefaultDriveIdProvider seam. No Graph client, no network, no
//           credential.
// RELATION: Exercises GraphDriveIdResolver only.
//           GraphDefaultDriveIdProvider is exercised by live validation.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Platform.Cloud.Providers;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace FileHarvester.Tests.Cloud.Providers;

/// <summary>Tests for GraphDriveIdResolver.</summary>
public sealed class GraphDriveIdResolverTests
{
    [Fact]
    public async Task ResolveAsyncReturnsTheProvidersDriveId()
    {
        var provider = Substitute.For<IDefaultDriveIdProvider>();
        provider.GetDefaultDriveIdAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("drive-id-1"));
        var resolver = new GraphDriveIdResolver();

        var id = await resolver.ResolveAsync(provider, CancellationToken.None);

        id.Should().Be("drive-id-1");
    }

    [Fact]
    public async Task ResolveAsyncThrowsWhenProviderReturnsNull()
    {
        var provider = Substitute.For<IDefaultDriveIdProvider>();
        provider.GetDefaultDriveIdAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));
        var resolver = new GraphDriveIdResolver();

        var act = async () => await resolver.ResolveAsync(provider, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ResolveAsyncThrowsWhenProviderReturnsEmpty()
    {
        var provider = Substitute.For<IDefaultDriveIdProvider>();
        provider.GetDefaultDriveIdAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(string.Empty));
        var resolver = new GraphDriveIdResolver();

        var act = async () => await resolver.ResolveAsync(provider, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ResolveAsyncThrowsWhenProviderIsNull()
    {
        var resolver = new GraphDriveIdResolver();

        var act = async () => await resolver.ResolveAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ResolveAsyncPropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var provider = Substitute.For<IDefaultDriveIdProvider>();
        provider.GetDefaultDriveIdAsync(Arg.Any<CancellationToken>())
            .Returns<Task<string?>>(_ => throw new OperationCanceledException());
        var resolver = new GraphDriveIdResolver();

        var act = async () => await resolver.ResolveAsync(provider, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
