// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/OAuth/CloudConnectionService.cs
// PURPOSE:  The implementation of ICloudConnectionService. Wraps the
//           internal flow registry, token store, and credential store. The
//           App never sees these internal types; it calls this service
//           through the public interface.
// RELATION: Implements ICloudConnectionService. The flow is selected per
//           call by config.ProviderId through IOAuthFlowRegistry.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Platform.Cloud.Credentials;

namespace FileHarvester.Platform.Cloud.OAuth;

/// <summary>The application-facing cloud connection implementation.</summary>
internal sealed class CloudConnectionService : ICloudConnectionService
{
    private readonly IOAuthFlowRegistry _flowRegistry;
    private readonly OAuthTokenStore _tokenStore;
    private readonly ITypedCredentialStore _credentialStore;

    /// <summary>Creates the service.</summary>
    /// <param name="flowRegistry">The per-provider flow registry. Not null.</param>
    /// <param name="tokenStore">The durable token store. Not null.</param>
    /// <param name="credentialStore">The typed credential store. Not null.</param>
    public CloudConnectionService(
        IOAuthFlowRegistry flowRegistry,
        OAuthTokenStore tokenStore,
        ITypedCredentialStore credentialStore)
    {
        _flowRegistry = flowRegistry ?? throw new ArgumentNullException(nameof(flowRegistry));
        _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
    }

    /// <inheritdoc />
    public async Task ConnectAsync(
        OAuthClientConfig config,
        CredentialKey key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(key);

        var flow = _flowRegistry.Resolve(config.ProviderId);
        var tokens = await flow.AuthorizeAsync(config, cancellationToken)
            .ConfigureAwait(false);

        var storeKey = CredentialStoreKey.OAuth(key.ProfileId, key.InstanceName);

        await _tokenStore.StoreAuthorizedAsync(storeKey, tokens, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(
        OAuthClientConfig config,
        CredentialKey key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(key);

        var storeKey = CredentialStoreKey.OAuth(key.ProfileId, key.InstanceName);

        await _tokenStore.RevokeAsync(config, storeKey, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListConnectedInstancesAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        return await _credentialStore
            .ListInstanceNamesAsync(providerId, CredentialKind.OAuth, cancellationToken)
            .ConfigureAwait(false);
    }
}
