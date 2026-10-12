// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Discovery/RemoteProviderSearchResolverTests.cs
// PURPOSE:  Tests the opt-in remote-search resolver: enabled/blank guards,
//           caching (hit, expiry, eviction, zero-TTL disables), resilience
//           fail-to-empty, cancellation propagation, and the cloud-search
//           policy routing. A hand-written backend fake records calls and
//           tokens; a substituted IResilienceExecutor invokes the operation.
// RELATION: Exercises FileHarvester.Platform.Cloud.Discovery.RemoteProviderSearchResolver.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Core.Composition;
using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Discovery;
using FluentAssertions;
using NSubstitute;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Errors;
using Xunit;

namespace FileHarvester.Tests.Cloud.Discovery;

/// <summary>
/// Tests for the remote-search resolver.
/// </summary>
public sealed class RemoteProviderSearchResolverTests
{
    private sealed class FakeBackend : IRemoteProviderSearchBackend
    {
        public int CallCount;
        public CancellationToken LastToken;
        public Func<string, CancellationToken, Task<IReadOnlyList<CloudNameCandidate>>> Handler
            = (_, _) => Task.FromResult<IReadOnlyList<CloudNameCandidate>>(
                Array.Empty<CloudNameCandidate>());

        public Task<IReadOnlyList<CloudNameCandidate>> SearchAsync(
            string userInput,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref CallCount);
            LastToken = cancellationToken;
            return Handler(userInput, cancellationToken);
        }
    }

    private static CloudNameCandidate Candidate(string id) =>
        new(
            CanonicalName: id,
            ProviderId: id,
            Aliases: new List<string> { id },
            AuthKind: AuthKind.LongLivedKey,
            OfficialUrl: null,
            Source: CloudNameSource.Remote,
            Confidence: CloudNameConfidence.Strong);

    private static IResilienceExecutor PassthroughExecutor()
    {
        var executor = Substitute.For<IResilienceExecutor>();
        executor.ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, Task<IReadOnlyList<CloudNameCandidate>>>>(),
            Arg.Any<Func<CancellationToken, Task<IReadOnlyList<CloudNameCandidate>>>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<Func<CancellationToken, Task<IReadOnlyList<CloudNameCandidate>>>>(1)(
                ci.ArgAt<CancellationToken>(5)));
        return executor;
    }

    private static RemoteProviderSearchOptions EnabledOptions(
        int ttlSeconds = 3600,
        int maxEntries = 256) =>
        new(
            Enabled: true,
            Endpoint: new Uri("https://example.invalid"),
            CredentialKeyName: null,
            CacheTtlSeconds: ttlSeconds,
            CacheMaxEntries: maxEntries,
            RateLimitPerMinute: 30);

    [Fact]
    public async Task DisabledReturnsEmptyWithoutCallingBackend()
    {
        var backend = new FakeBackend();
        var sut = new RemoteProviderSearchResolver(
            backend, RemoteProviderSearchOptions.Default, PassthroughExecutor());

        var result = await sut.SearchAsync("google", CancellationToken.None);

        result.Should().BeEmpty();
        backend.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task BlankInputReturnsEmptyWithoutCallingBackend()
    {
        var backend = new FakeBackend();
        var sut = new RemoteProviderSearchResolver(
            backend, EnabledOptions(), PassthroughExecutor());

        var result = await sut.SearchAsync("   ", CancellationToken.None);

        result.Should().BeEmpty();
        backend.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task EnabledCallsBackendOncePerUncachedInput()
    {
        var backend = new FakeBackend();
        backend.Handler = (_, _) => Task.FromResult<IReadOnlyList<CloudNameCandidate>>(
            new[] { Candidate("gdrive") });
        var sut = new RemoteProviderSearchResolver(
            backend, EnabledOptions(), PassthroughExecutor());

        var result = await sut.SearchAsync("google", CancellationToken.None);

        result.Should().HaveCount(1);
        backend.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SecondCallWithinTtlDoesNotCallBackend()
    {
        var backend = new FakeBackend();
        backend.Handler = (_, _) => Task.FromResult<IReadOnlyList<CloudNameCandidate>>(
            new[] { Candidate("gdrive") });
        var sut = new RemoteProviderSearchResolver(
            backend, EnabledOptions(ttlSeconds: 60), PassthroughExecutor());

        await sut.SearchAsync("google", CancellationToken.None);
        await sut.SearchAsync("google", CancellationToken.None);

        backend.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ExpiredEntryCallsBackendAgain()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var backend = new FakeBackend();
        backend.Handler = (_, _) => Task.FromResult<IReadOnlyList<CloudNameCandidate>>(
            new[] { Candidate("gdrive") });
        var sut = new RemoteProviderSearchResolver(
            backend, EnabledOptions(ttlSeconds: 60), PassthroughExecutor(),
            clock: () => now);

        await sut.SearchAsync("google", CancellationToken.None);
        now = now.AddSeconds(61);
        await sut.SearchAsync("google", CancellationToken.None);

        backend.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task ZeroTtlDisablesCaching()
    {
        var backend = new FakeBackend();
        backend.Handler = (_, _) => Task.FromResult<IReadOnlyList<CloudNameCandidate>>(
            new[] { Candidate("gdrive") });
        var sut = new RemoteProviderSearchResolver(
            backend, EnabledOptions(ttlSeconds: 0), PassthroughExecutor());

        await sut.SearchAsync("google", CancellationToken.None);
        await sut.SearchAsync("google", CancellationToken.None);

        backend.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task DistinctInputsCallBackendOnceEach()
    {
        var backend = new FakeBackend();
        backend.Handler = (input, _) => Task.FromResult<IReadOnlyList<CloudNameCandidate>>(
            new[] { Candidate(input) });
        var sut = new RemoteProviderSearchResolver(
            backend, EnabledOptions(), PassthroughExecutor());

        await sut.SearchAsync("google", CancellationToken.None);
        await sut.SearchAsync("microsoft", CancellationToken.None);

        backend.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task CacheEvictsOldestWhenMaxEntriesExceeded()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var backend = new FakeBackend();
        backend.Handler = (input, _) => Task.FromResult<IReadOnlyList<CloudNameCandidate>>(
            new[] { Candidate(input) });
        var sut = new RemoteProviderSearchResolver(
            backend, EnabledOptions(ttlSeconds: 60, maxEntries: 2), PassthroughExecutor(),
            clock: () => now);

        await sut.SearchAsync("a", CancellationToken.None);
        now = now.AddSeconds(1);
        await sut.SearchAsync("b", CancellationToken.None);
        now = now.AddSeconds(1);
        await sut.SearchAsync("c", CancellationToken.None);

        backend.CallCount.Should().Be(3);

        await sut.SearchAsync("b", CancellationToken.None);
        await sut.SearchAsync("c", CancellationToken.None);
        backend.CallCount.Should().Be(3);

        await sut.SearchAsync("a", CancellationToken.None);
        backend.CallCount.Should().Be(4);
    }

    [Fact]
    public async Task BackendThrowingResilienceExceptionReturnsEmpty()
    {
        var backend = new FakeBackend();
        backend.Handler = (_, _) => throw new ResilienceException(
            "test-policy",
            "test",
            ResilienceErrorCategory.CircuitOpen,
            0,
            TimeSpan.Zero,
            null,
            null,
            null);
        var sut = new RemoteProviderSearchResolver(
            backend, EnabledOptions(), PassthroughExecutor());

        var result = await sut.SearchAsync("google", CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task BackendThrowingNonResilienceExceptionPropagates()
    {
        var backend = new FakeBackend();
        backend.Handler = (_, _) => throw new InvalidOperationException("backend defect");
        var sut = new RemoteProviderSearchResolver(
            backend, EnabledOptions(), PassthroughExecutor());

        var act = async () => await sut.SearchAsync("google", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CancellationBeforeCallPropagates()
    {
        var backend = new FakeBackend();
        var sut = new RemoteProviderSearchResolver(
            backend, EnabledOptions(), PassthroughExecutor());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await sut.SearchAsync("google", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CancellationDuringCallPropagates()
    {
        var backend = new FakeBackend();
        backend.Handler = (_, ct) => throw new OperationCanceledException(ct);
        var sut = new RemoteProviderSearchResolver(
            backend, EnabledOptions(), PassthroughExecutor());
        using var cts = new CancellationTokenSource();

        var act = async () => await sut.SearchAsync("google", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CloudSearchPolicyNameIsPassedToResilienceExecutor()
    {
        var backend = new FakeBackend();
        backend.Handler = (_, _) => Task.FromResult<IReadOnlyList<CloudNameCandidate>>(
            Array.Empty<CloudNameCandidate>());
        var executor = PassthroughExecutor();
        var sut = new RemoteProviderSearchResolver(
            backend, EnabledOptions(), executor);

        await sut.SearchAsync("google", CancellationToken.None);

        await executor.Received(1).ExecuteAsync(
            Arg.Is<string>(n => n == PolicyNames.CloudSearch),
            Arg.Any<Func<CancellationToken, Task<IReadOnlyList<CloudNameCandidate>>>>(),
            Arg.Any<Func<CancellationToken, Task<IReadOnlyList<CloudNameCandidate>>>>(),
            Arg.Any<string?>(),
            Arg.Any<int?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BackendReceivesSameCancellationToken()
    {
        var backend = new FakeBackend();
        backend.Handler = (_, _) => Task.FromResult<IReadOnlyList<CloudNameCandidate>>(
            Array.Empty<CloudNameCandidate>());
        var sut = new RemoteProviderSearchResolver(
            backend, EnabledOptions(), PassthroughExecutor());
        using var cts = new CancellationTokenSource();

        await sut.SearchAsync("google", cts.Token);

        backend.LastToken.Should().Be(cts.Token);
    }
}
