// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/OAuth/OAuthTokenStore.cs
// PURPOSE:  Splits the durable refresh token from the ephemeral access token.
//           The refresh token lives only in ITypedCredentialStore under an
//           OAuth-namespaced key; the access token lives only in an
//           in-memory cache. The flow is resolved per request from the
//           IOAuthFlowRegistry by the config provider id, so the store
//           serves Google Drive and OneDrive without knowing which it is.
// RELATION: Consumes ITypedCredentialStore and IOAuthFlowRegistry.
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Platform.Cloud.Credentials;

namespace FileHarvester.Platform.Cloud.OAuth;

/// <summary>The durable-refresh-token / ephemeral-access-token split.</summary>
internal sealed class OAuthTokenStore : IDisposable
{
    public static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(5);

    private readonly ITypedCredentialStore _store;
    private readonly IOAuthFlowRegistry _flowRegistry;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly ConcurrentDictionary<CredentialStoreKey, CachedAccessToken> _cache = new();
    private bool _disposed;

    /// <summary>Creates the store over a credential store and a flow registry.</summary>
    /// <param name="store">The durable typed credential store. Not null.</param>
    /// <param name="flowRegistry">The per-provider flow registry. Not null.</param>
    /// <param name="now">The clock. Null uses UTC now.</param>
    public OAuthTokenStore(
        ITypedCredentialStore store,
        IOAuthFlowRegistry flowRegistry,
        Func<DateTimeOffset>? now = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _flowRegistry = flowRegistry ?? throw new ArgumentNullException(nameof(flowRegistry));
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Disposes the refresh semaphore.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refreshLock.Dispose();
    }

    private sealed record CachedAccessToken(string AccessToken, DateTime ExpiresAtUtc);

    /// <summary>Returns an access token for the key, refreshing if needed.</summary>
    public async Task<OAuthTokens> GetAccessTokenAsync(
        OAuthClientConfig config,
        CredentialStoreKey key,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (_cache.TryGetValue(key, out var cached) && IsUsable(cached))
        {
            return AccessOnly(cached);
        }

        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(key, out var rechecked) && IsUsable(rechecked))
            {
                return AccessOnly(rechecked);
            }

            var refreshToken = await _store.ReadAsync(key, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(refreshToken))
            {
                throw new OAuthTokenRevokedException();
            }

            var flow = _flowRegistry.Resolve(config.ProviderId);
            var refreshed = await flow.RefreshAsync(config, refreshToken, ct).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(refreshed.RefreshToken))
            {
                await _store.WriteAsync(key, refreshed.RefreshToken, ct).ConfigureAwait(false);
            }

            _cache[key] = new CachedAccessToken(refreshed.AccessToken, refreshed.ExpiresAtUtc);
            return AccessOnly(_cache[key]);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Stores the durable refresh token and caches the access token.</summary>
    public async Task StoreAuthorizedAsync(
        CredentialStoreKey key,
        OAuthTokens tokens,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        if (string.IsNullOrEmpty(tokens.RefreshToken))
        {
            throw new ArgumentException(
                "First authorization must carry a refresh token.",
                nameof(tokens));
        }

        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _store.WriteAsync(key, tokens.RefreshToken, ct).ConfigureAwait(false);
            _cache[key] = new CachedAccessToken(tokens.AccessToken, tokens.ExpiresAtUtc);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Forces a refresh regardless of the current cache.</summary>
    public async Task<OAuthTokens> RefreshAsync(
        OAuthClientConfig config,
        CredentialStoreKey key,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);

        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var refreshToken = await _store.ReadAsync(key, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(refreshToken))
            {
                throw new OAuthTokenRevokedException();
            }

            var flow = _flowRegistry.Resolve(config.ProviderId);
            var refreshed = await flow.RefreshAsync(config, refreshToken, ct).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(refreshed.RefreshToken))
            {
                await _store.WriteAsync(key, refreshed.RefreshToken, ct).ConfigureAwait(false);
            }

            _cache[key] = new CachedAccessToken(refreshed.AccessToken, refreshed.ExpiresAtUtc);
            return AccessOnly(_cache[key]);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Revokes the durable token at the provider, then deletes it. If the
    /// flow revoke fails, the durable token is preserved.
    /// </summary>
    public async Task RevokeAsync(
        OAuthClientConfig config,
        CredentialStoreKey key,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);

        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var refreshToken = await _store.ReadAsync(key, ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(refreshToken))
            {
                var flow = _flowRegistry.Resolve(config.ProviderId);
                await flow.RevokeAsync(config, refreshToken, ct).ConfigureAwait(false);
                await _store.DeleteAsync(key, ct).ConfigureAwait(false);
            }

            _cache.TryRemove(key, out _);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool IsUsable(CachedAccessToken cached)
        => cached.ExpiresAtUtc - RefreshWindow > _now().UtcDateTime;

    private static OAuthTokens AccessOnly(CachedAccessToken cached)
        => new(cached.AccessToken, cached.ExpiresAtUtc, RefreshToken: null, Scope: null);
}
