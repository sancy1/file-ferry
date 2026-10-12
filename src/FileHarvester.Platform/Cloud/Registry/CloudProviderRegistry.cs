// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Registry/CloudProviderRegistry.cs
// PURPOSE:  The runtime provider registry. One Registration per provider,
//           keyed by provider id and (when the kind has one) by path scheme.
//           Resolution returns a CloudProviderResolution: a provider, or a
//           safe credential-free failure reason. A typed credential
//           exception is mapped to its reason; an unexpected construction
//           fault is mapped to ProviderConstructionFailed.
// RELATION: Implements ICloudProviderRegistry. Registered by
//           RegistryServiceCollectionExtensions.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Core.Domain;

namespace FileHarvester.Platform.Cloud.Registry;

/// <summary>
/// The runtime provider registry implementation.
/// </summary>
public sealed class CloudProviderRegistry : ICloudProviderRegistry
{
    private readonly ConcurrentDictionary<PathSchemeKind, Registration> _byScheme = new();
    private readonly ConcurrentDictionary<string, Registration> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _profilesLock = new();
    private List<ProviderProfile> _profiles = new();

    /// <inheritdoc />
    public void Register(
        ProviderProfile profile,
        Func<string?, CancellationToken, Task<ICloudProvider>> factory)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(factory);

        var scheme = MapKindToScheme(profile.Kind);
        var registration = new Registration(profile.Id, scheme, factory);

        // The last registration wins: both keys are overwritten.
        _byId[profile.Id] = registration;
        if (scheme is not null)
        {
            _byScheme[scheme.Value] = registration;
        }

        lock (_profilesLock)
        {
            _profiles.RemoveAll(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
            _profiles.Add(profile);
        }
    }

    /// <inheritdoc />
    public async Task<CloudProviderResolution> ResolveAsync(
        PathSchemeKind scheme,
        string? instance,
        CancellationToken cancellationToken = default)
    {
        if (!_byScheme.TryGetValue(scheme, out var registration))
        {
            return CloudProviderResolution.Failed(CloudResolutionFailure.NoProviderRegistered);
        }

        return await ResolveRegistrationAsync(registration, instance, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CloudProviderResolution> ResolveAsync(
        string providerId,
        string? instance,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return CloudProviderResolution.Failed(CloudResolutionFailure.NoProviderRegistered);
        }

        if (!_byId.TryGetValue(providerId, out var registration))
        {
            return CloudProviderResolution.Failed(CloudResolutionFailure.NoProviderRegistered);
        }

        return await ResolveRegistrationAsync(registration, instance, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IReadOnlyList<ProviderProfile> Profiles
    {
        get
        {
            lock (_profilesLock)
            {
                return _profiles.ToArray();
            }
        }
    }

    // ------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------

    /// <summary>
    /// Resolves a registration for an instance. A typed credential
    /// exception is mapped to its reason; any other construction fault is
    /// mapped to ProviderConstructionFailed. A failure is not cached, so the
    /// next resolution through either key retries the same factory.
    /// </summary>
    private static async Task<CloudProviderResolution> ResolveRegistrationAsync(
        Registration registration,
        string? instance,
        CancellationToken cancellationToken)
    {
        try
        {
            var provider = await registration.GetOrCreateAsync(instance, cancellationToken)
                .ConfigureAwait(false);
            return CloudProviderResolution.Resolved(provider);
        }
        catch (CloudCredentialResolutionException ex)
        {
            return CloudProviderResolution.Failed(ex.Reason);
        }
        catch (Exception)
        {
            return CloudProviderResolution.Failed(CloudResolutionFailure.ProviderConstructionFailed);
        }
    }

    /// <summary>
    /// The scheme for a provider kind, or null for a kind with no scheme.
    /// </summary>
    private static PathSchemeKind? MapKindToScheme(CloudProviderKind kind) => kind switch
    {
        CloudProviderKind.S3Compatible => PathSchemeKind.S3,
        CloudProviderKind.AzureBlob    => PathSchemeKind.AzureBlob,
        CloudProviderKind.GcsObject    => PathSchemeKind.Gcs,
        CloudProviderKind.GoogleDrive  => PathSchemeKind.GoogleDrive,
        CloudProviderKind.Dropbox      => PathSchemeKind.Dropbox,
        CloudProviderKind.OneDrive     => PathSchemeKind.OneDrive,
        _                              => null,
    };

    /// <summary>
    /// One registration: the provider id, the optional scheme, the
    /// asynchronous instance-aware factory, and a per-registration lock
    /// guarding a cache keyed by instance. A successful construction is
    /// cached; a failed construction is not, so a later call retries.
    /// </summary>
    private sealed class Registration
    {
        private readonly object _lock = new();

        // The instance key is never null: the no-instance case uses a
        // dedicated field, because Dictionary<TKey, TValue> requires a
        // non-null TKey. Instance names are validated non-empty by the
        // credential store, so a real instance can never collide with
        // the null case.
        private readonly Dictionary<string, ICloudProvider> _byInstance = new(StringComparer.Ordinal);
        private ICloudProvider? _noInstanceProvider;

        public Registration(
            string providerId,
            PathSchemeKind? scheme,
            Func<string?, CancellationToken, Task<ICloudProvider>> factory)
        {
            ProviderId = providerId;
            Scheme = scheme;
            Factory = factory;
        }

        public string ProviderId { get; }
        public PathSchemeKind? Scheme { get; }
        public Func<string?, CancellationToken, Task<ICloudProvider>> Factory { get; }

        /// <summary>
        /// Returns the cached provider for the instance, constructing it
        /// once on success. A failed construction is not cached and
        /// rethrows; the next call retries the factory.
        /// </summary>
        public async Task<ICloudProvider> GetOrCreateAsync(
            string? instance,
            CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                if (instance is null)
                {
                    if (_noInstanceProvider is not null) return _noInstanceProvider;
                }
                else if (_byInstance.TryGetValue(instance, out var cached))
                {
                    return cached;
                }
            }

            // Construct outside the lock: the factory is asynchronous and
            // must not hold a monitor across an await. Concurrent callers
            // for the same instance may both construct; the first to
            // finish writes the cache, and the second overwrites with an
            // equivalent provider. The contract here is "at most one
            // cached provider per instance, retried on failure."
            var created = await Factory(instance, cancellationToken)
                .ConfigureAwait(false);

            lock (_lock)
            {
                if (instance is null)
                {
                    _noInstanceProvider = created;
                }
                else
                {
                    _byInstance[instance] = created;
                }
            }

            return created;
        }
    }
}
