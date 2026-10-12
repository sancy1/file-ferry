// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/OAuth/OAuthTokenStoreTests.cs
// PURPOSE:  Unit tests for OAuthTokenStore. Uses an in-memory
//           ITypedCredentialStore stub, an NSubstitute IOAuthFlow, and an
//           injected clock. Covers cache-hit/miss behavior, the refresh
//           window, rotation safety, revoke safety, and single-flight
//           refresh under concurrency.
// RELATION: Exercises FileHarvester.Platform.Cloud.OAuth.OAuthTokenStore.
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Platform.Cloud.Credentials;
using FileHarvester.Platform.Cloud.OAuth;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace FileHarvester.Tests.Cloud.OAuth;

/// <summary>
/// Tests for <see cref="OAuthTokenStore"/>.
/// </summary>
public sealed class OAuthTokenStoreTests
{
    private const string ProviderId = "google-drive";
    private const string InstanceName = "test-instance";

    private static OAuthClientConfig Config() => new(
        ProviderId: ProviderId,
        ClientId: "placeholder-client-id",
        AuthorizationEndpoint: "https://accounts.google.com/o/oauth2/v2/auth",
        TokenEndpoint: "https://oauth2.googleapis.com/token",
        Scopes: new List<string> { OAuthScopes.DriveFile },
        RedirectUri: "http://127.0.0.1:53682/callback/");

    // The token store is OAuth-only. Keys passed to it are OAuth-typed.
    private static CredentialStoreKey Key() =>
        CredentialStoreKey.OAuth(ProviderId, InstanceName);

    // ---- In-memory ITypedCredentialStore stub --------------------------

    private sealed class InMemoryCredentialStore : ITypedCredentialStore
    {
        private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

        public bool ThrowOnWrite { get; set; }
        public int WriteCount { get; private set; }
        public int DeleteCount { get; private set; }

        public Task WriteAsync(CredentialStoreKey key, string secret, CancellationToken cancellationToken = default)
        {
            WriteCount++;
            if (ThrowOnWrite)
            {
                throw new InvalidOperationException("store write failed");
            }
            _values[key.ToStoreKey()] = secret;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(CredentialStoreKey key, CancellationToken cancellationToken = default)
            => Task.FromResult(_values.TryGetValue(key.ToStoreKey(), out var v) ? v : null);

        public Task DeleteAsync(CredentialStoreKey key, CancellationToken cancellationToken = default)
        {
            DeleteCount++;
            _values.TryRemove(key.ToStoreKey(), out _);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListInstanceNamesAsync(string profileId, CredentialKind kind, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    [Fact]
    public async Task GetAccessTokenRefreshesWhenCacheEmpty()
    {
        var store = new InMemoryCredentialStore();
        await store.WriteAsync(Key(), "the-refresh-token");
        var flow = Substitute.For<IOAuthFlow>();
        flow.RefreshAsync(Arg.Any<OAuthClientConfig>(), "the-refresh-token", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OAuthTokens("access-1", DateTime.UtcNow.AddHours(1), RefreshToken: null, Scope: null)));

        var sut = new OAuthTokenStore(store, new SingleFlowRegistry(flow));
        var result = await sut.GetAccessTokenAsync(Config(), Key(), CancellationToken.None);

        result.AccessToken.Should().Be("access-1");
        result.RefreshToken.Should().BeNull();
        await flow.Received(1).RefreshAsync(Arg.Any<OAuthClientConfig>(), "the-refresh-token", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAccessTokenReturnsCachedWhenOutsideRefreshWindow()
    {
        var store = new InMemoryCredentialStore();
        await store.WriteAsync(Key(), "the-refresh-token");
        var flow = Substitute.For<IOAuthFlow>();
        flow.RefreshAsync(Arg.Any<OAuthClientConfig>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OAuthTokens("access-1", DateTime.UtcNow.AddHours(1), RefreshToken: null, Scope: null)));

        var sut = new OAuthTokenStore(store, new SingleFlowRegistry(flow));
        await sut.GetAccessTokenAsync(Config(), Key(), CancellationToken.None);
        var second = await sut.GetAccessTokenAsync(Config(), Key(), CancellationToken.None);

        second.AccessToken.Should().Be("access-1");
        await flow.Received(1).RefreshAsync(Arg.Any<OAuthClientConfig>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAccessTokenRefreshesWhenInsideRefreshWindow()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new InMemoryCredentialStore();
        await store.WriteAsync(Key(), "the-refresh-token");
        var flow = Substitute.For<IOAuthFlow>();
        flow.RefreshAsync(Arg.Any<OAuthClientConfig>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OAuthTokens("access-1", now.UtcDateTime.AddMinutes(3), RefreshToken: null, Scope: null)));

        var sut = new OAuthTokenStore(store, new SingleFlowRegistry(flow), () => now);
        await sut.GetAccessTokenAsync(Config(), Key(), CancellationToken.None);

        var second = await sut.GetAccessTokenAsync(Config(), Key(), CancellationToken.None);

        await flow.Received(2).RefreshAsync(Arg.Any<OAuthClientConfig>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAccessTokenThrowsWhenNoStoredRefreshToken()
    {
        var store = new InMemoryCredentialStore();
        var flow = Substitute.For<IOAuthFlow>();

        var sut = new OAuthTokenStore(store, new SingleFlowRegistry(flow));
        var act = async () => await sut.GetAccessTokenAsync(Config(), Key(), CancellationToken.None);

        await act.Should().ThrowAsync<OAuthTokenRevokedException>();
        await flow.DidNotReceive().RefreshAsync(Arg.Any<OAuthClientConfig>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreAuthorizedWritesRefreshTokenAndCachesAccessToken()
    {
        var store = new InMemoryCredentialStore();
        var flow = Substitute.For<IOAuthFlow>();
        var sut = new OAuthTokenStore(store, new SingleFlowRegistry(flow));

        var tokens = new OAuthTokens("access-1", DateTime.UtcNow.AddHours(1), "refresh-1", "scope");
        await sut.StoreAuthorizedAsync(Key(), tokens, CancellationToken.None);

        (await store.ReadAsync(Key())).Should().Be("refresh-1");
        var fetched = await sut.GetAccessTokenAsync(Config(), Key(), CancellationToken.None);
        fetched.AccessToken.Should().Be("access-1");
        fetched.RefreshToken.Should().BeNull();
    }

    [Fact]
    public async Task StoreAuthorizedRejectsTokenWithoutRefreshToken()
    {
        var store = new InMemoryCredentialStore();
        var flow = Substitute.For<IOAuthFlow>();
        var sut = new OAuthTokenStore(store, new SingleFlowRegistry(flow));

        var tokens = new OAuthTokens("access-1", DateTime.UtcNow.AddHours(1), RefreshToken: null, Scope: null);
        var act = async () => await sut.StoreAuthorizedAsync(Key(), tokens, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        store.WriteCount.Should().Be(0);
    }

    [Fact]
    public async Task RotationWriteFailurePreservesOldToken()
    {
        var store = new InMemoryCredentialStore();
        await store.WriteAsync(Key(), "old-refresh");
        var flow = Substitute.For<IOAuthFlow>();
        flow.RefreshAsync(Arg.Any<OAuthClientConfig>(), "old-refresh", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OAuthTokens("access-1", DateTime.UtcNow.AddHours(1), "new-refresh", null)));

        store.ThrowOnWrite = true;

        var sut = new OAuthTokenStore(store, new SingleFlowRegistry(flow));
        var act = async () => await sut.GetAccessTokenAsync(Config(), Key(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        store.ThrowOnWrite = false;
        (await store.ReadAsync(Key())).Should().Be("old-refresh");
    }

    [Fact]
    public async Task RevokeFailurePreservesDurableToken()
    {
        var store = new InMemoryCredentialStore();
        await store.WriteAsync(Key(), "the-refresh-token");
        var flow = Substitute.For<IOAuthFlow>();
        flow.RevokeAsync(Arg.Any<OAuthClientConfig>(), "the-refresh-token", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("revoke failed")));

        var sut = new OAuthTokenStore(store, new SingleFlowRegistry(flow));
        var act = async () => await sut.RevokeAsync(Config(), Key(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        store.DeleteCount.Should().Be(0);
        (await store.ReadAsync(Key())).Should().Be("the-refresh-token");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    public async Task ConcurrentGetAccessTokenCallsRefreshOnce(int concurrency)
    {
        var store = new InMemoryCredentialStore();
        await store.WriteAsync(Key(), "the-refresh-token");
        var flow = Substitute.For<IOAuthFlow>();
        var refreshCount = 0;
        flow.RefreshAsync(Arg.Any<OAuthClientConfig>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref refreshCount);
                return Task.FromResult(new OAuthTokens("access-1", DateTime.UtcNow.AddHours(1), RefreshToken: null, Scope: null));
            });

        var sut = new OAuthTokenStore(store, new SingleFlowRegistry(flow));

        var tasks = new Task<OAuthTokens>[concurrency];
        for (var i = 0; i < concurrency; i++)
        {
            tasks[i] = sut.GetAccessTokenAsync(Config(), Key(), CancellationToken.None);
        }

        var results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r.AccessToken == "access-1");
        refreshCount.Should().Be(1);
    }
}
