// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Discovery/RemoteProviderSearchResolver.cs
// PURPOSE:  The opt-in remote provider-search resolver. Wraps a backend
//           with the cloud-search resilience policy, a bounded TTL cache,
//           and a fail-to-empty contract on resilience failure.
// RELATION: Implements IRemoteProviderSearch. Consumes
//           IRemoteProviderSearchBackend, RemoteProviderSearchOptions, and
//           IResilienceExecutor. Not registered in the running app in
//           Step 6 (F-088).
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Core.Composition;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Errors;

namespace FileHarvester.Platform.Cloud.Discovery;

/// <summary>
/// The opt-in remote provider-search resolver.
/// </summary>
/// <remarks>
/// <para>
/// Contract (mirrors IRemoteProviderSearch): blank input returns empty
/// without contacting the backend; a resilience failure returns empty;
/// cancellation propagates; any other backend exception propagates.
/// </para>
/// <para>
/// Caching: CacheTtlSeconds of zero disables caching (every call contacts
/// the backend); a positive value caches results for that many seconds.
/// When the cache exceeds CacheMaxEntries, the oldest-expiring entry is
/// evicted on the next insert. Eviction is best-effort under concurrency;
/// the cache may briefly hold more than CacheMaxEntries under contention.
/// </para>
/// <para>
/// The resolver does not re-stamp Source or Confidence on results from
/// the backend; the backend owns those fields.
/// </para>
/// </remarks>
public sealed class RemoteProviderSearchResolver : IRemoteProviderSearch
{
    private static readonly IReadOnlyList<CloudNameCandidate> Empty =
        Array.Empty<CloudNameCandidate>();

    private readonly IRemoteProviderSearchBackend _backend;
    private readonly RemoteProviderSearchOptions _options;
    private readonly IResilienceExecutor _resilience;
    private readonly Func<DateTimeOffset> _clock;

    private readonly ConcurrentDictionary<
        string,
        CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates the resolver.
    /// </summary>
    /// <param name="backend">The permitted-API backend. Not null.</param>
    /// <param name="options">The settings. Not null.</param>
    /// <param name="resilience">The resilience executor. Not null.</param>
    /// <param name="clock">
    /// Optional clock seam. Defaults to the system UTC clock. Tests supply
    /// a controllable clock so expiry and eviction are deterministic.
    /// </param>
    public RemoteProviderSearchResolver(
        IRemoteProviderSearchBackend backend,
        RemoteProviderSearchOptions options,
        IResilienceExecutor resilience,
        Func<DateTimeOffset>? clock = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _resilience = resilience ?? throw new ArgumentNullException(nameof(resilience));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CloudNameCandidate>> SearchAsync(
        string userInput,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Empty;
        }

        if (string.IsNullOrWhiteSpace(userInput))
        {
            return Empty;
        }

        var needle = userInput.Trim();
        var now = _clock();
        cancellationToken.ThrowIfCancellationRequested();
        var cachingEnabled = _options.CacheTtlSeconds > 0;

        if (cachingEnabled && TryGetFromCache(needle, now, out var cached))
        {
            return cached;
        }

        IReadOnlyList<CloudNameCandidate> result;
        try
        {
            result = await _resilience.ExecuteAsync(
                policyName: PolicyNames.CloudSearch,
                operation: token => _backend.SearchAsync(needle, token),
                fallback: _ => Task.FromResult(Empty),
                ct: cancellationToken).ConfigureAwait(false);
        }
        catch (ResilienceException)
        {
            // Resilience failure: timeout or circuit-open. Fail to empty,
            // never to a throw, so a caller never sees a remote failure
            // as a local error. Cancellation is not caught here; it
            // propagates. Other backend exceptions propagate as well.
            return Empty;
        }

        var normalized = result ?? Empty;

        if (cachingEnabled)
        {
            StoreInCache(needle, normalized, now);
        }

        return normalized;
    }

    private bool TryGetFromCache(
        string key,
        DateTimeOffset now,
        out IReadOnlyList<CloudNameCandidate> cached)
    {
        cached = Empty;

        if (!_cache.TryGetValue(key, out var entry))
        {
            return false;
        }

        if (entry.ExpiresAt <= now)
        {
            _cache.TryRemove(key, out _);
            return false;
        }

        cached = entry.Result;
        return true;
    }

    private void StoreInCache(
        string key,
        IReadOnlyList<CloudNameCandidate> result,
        DateTimeOffset now)
    {
        var entry = new CacheEntry(
            Result: result,
            ExpiresAt: now.AddSeconds(_options.CacheTtlSeconds));

        _cache[key] = entry;

        if (_cache.Count > _options.CacheMaxEntries)
        {
            EvictOldestExpiring();
        }
    }

    private void EvictOldestExpiring()
    {
        string? oldestKey = null;
        var oldestExpiry = DateTimeOffset.MaxValue;

        foreach (var pair in _cache)
        {
            if (pair.Value.ExpiresAt < oldestExpiry)
            {
                oldestExpiry = pair.Value.ExpiresAt;
                oldestKey = pair.Key;
            }
        }

        if (oldestKey is not null)
        {
            _cache.TryRemove(oldestKey, out _);
        }
    }

    private readonly record struct CacheEntry(
        IReadOnlyList<CloudNameCandidate> Result,
        DateTimeOffset ExpiresAt);
}
