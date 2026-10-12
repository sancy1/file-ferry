// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Registry/ICloudProviderRegistry.cs
// PURPOSE:  The runtime registry of cloud providers. Resolves a provider
//           by URI scheme and optional instance (the dispatcher path),
//           or by provider id and optional instance (the UI path).
//           Resolution returns a CloudProviderResolution: either a
//           provider, or a safe credential-free failure reason.
// RELATION: Implemented by CloudProviderRegistry. Consumed by the
//           dispatcher delegate and, in later steps, by the profile
//           and connection flows.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Core.Domain;

namespace FileHarvester.Platform.Cloud.Registry;

/// <summary>
/// The runtime provider registry.
/// </summary>
/// <remarks>
/// <para>
/// A provider is registered by profile and an asynchronous, instance-aware
/// factory. Resolution is lazy: the factory is invoked once per
/// (registration, instance) pair, on first resolution. A failed
/// construction is not cached; a later resolution retries the factory.
/// </para>
/// <para>
/// A duplicate registration for the same provider id or scheme replaces
/// the previous entry. The last registration wins.
/// </para>
/// <para>
/// The factory, not the registry, applies the credential instance rule:
/// a null instance means the profile must have exactly one stored
/// credential; a named instance must exist. The registry only passes
/// the instance through; it never reads credentials.
/// </para>
/// </remarks>
public interface ICloudProviderRegistry
{
    /// <summary>
    /// Registers a provider. A second registration for the same id or
    /// scheme replaces the first.
    /// </summary>
    /// <param name="profile">The provider profile. Not null.</param>
    /// <param name="factory">
    /// The asynchronous factory that constructs the provider on first
    /// resolution. Not null. It receives the optional instance name and a
    /// cancellation token, and it applies the credential instance rule.
    /// It is invoked at most once per (registration, instance) on success.
    /// </param>
    void Register(
        ProviderProfile profile,
        Func<string?, CancellationToken, Task<ICloudProvider>> factory);

    /// <summary>
    /// Resolves a provider by scheme and optional instance.
    /// </summary>
    /// <param name="scheme">The scheme.</param>
    /// <param name="instance">
    /// The instance name, or null for the scheme-only path.
    /// </param>
    /// <param name="cancellationToken">Cancels the resolve.</param>
    /// <returns>
    /// A CloudProviderResolution: the provider, or a safe failure reason.
    /// </returns>
    Task<CloudProviderResolution> ResolveAsync(
        PathSchemeKind scheme,
        string? instance,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a provider by provider id and optional instance.
    /// Case-insensitive on the provider id.
    /// </summary>
    /// <param name="providerId">The provider id (for example aws-s3).</param>
    /// <param name="instance">
    /// The instance name, or null for the provider-only path.
    /// </param>
    /// <param name="cancellationToken">Cancels the resolve.</param>
    /// <returns>
    /// A CloudProviderResolution: the provider, or a safe failure reason.
    /// </returns>
    Task<CloudProviderResolution> ResolveAsync(
        string providerId,
        string? instance,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The registered profiles, in registration order. Never null; may be
    /// empty.
    /// </summary>
    IReadOnlyList<ProviderProfile> Profiles { get; }
}
