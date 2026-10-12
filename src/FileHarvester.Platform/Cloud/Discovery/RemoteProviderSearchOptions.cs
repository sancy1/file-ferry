// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Discovery/RemoteProviderSearchOptions.cs
// PURPOSE:  Settings for the opt-in remote provider search. Carries no
//           secret; CredentialKeyName is a lookup name, never a value.
// RELATION: Consumed by RemoteProviderSearchResolver and
//           RemoteProviderSearchValidator. Not registered in DI and not
//           bound to app configuration in Step 6 (F-090).
// ============================================================================

using System;

namespace FileHarvester.Platform.Cloud.Discovery;

/// <summary>
/// Settings for the opt-in remote provider search.
/// </summary>
/// <param name="Enabled">
/// True to enable remote search. False disables the resolver entirely;
/// it returns empty without contacting the backend.
/// </param>
/// <param name="Endpoint">
/// The permitted search API endpoint, or null when the backend does not
/// require one. A backend that requires an endpoint is a configuration
/// error when this is null and Enabled is true.
/// </param>
/// <param name="CredentialKeyName">
/// The name of the credential-store key the backend reads its API
/// credential from, or null when the backend needs none. This is a NAME,
/// never a credential value. The value itself lives only in the platform
/// credential store.
/// </param>
/// <param name="CacheTtlSeconds">
/// Cache time-to-live in seconds. Zero means caching is disabled: every
/// call contacts the backend. A positive value means an entry is served
/// from cache until it expires. Negative values are treated as zero.
/// </param>
/// <param name="CacheMaxEntries">
/// Maximum entries held in the in-memory cache. When the cache exceeds
/// this count, the oldest-expiring entry is evicted on the next insert.
/// Values less than one are treated as one.
/// </param>
/// <param name="RateLimitPerMinute">
/// Per-minute call ceiling advertised to a backend. Not enforced by the
/// resolver in Step 6; the cloud-search policy owns rate limiting.
/// Values less than one are treated as one.
/// </param>
public sealed record RemoteProviderSearchOptions(
    bool Enabled,
    Uri? Endpoint,
    string? CredentialKeyName,
    int CacheTtlSeconds,
    int CacheMaxEntries,
    int RateLimitPerMinute)
{
    /// <summary>
    /// The disabled default: Enabled false, no endpoint, no credential
    /// key, 3600-second TTL, 256-entry cache, 30 calls per minute.
    /// </summary>
    public static RemoteProviderSearchOptions Default { get; } =
        new(
            Enabled: false,
            Endpoint: null,
            CredentialKeyName: null,
            CacheTtlSeconds: 3600,
            CacheMaxEntries: 256,
            RateLimitPerMinute: 30);
}
