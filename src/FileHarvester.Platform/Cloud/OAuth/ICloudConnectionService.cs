// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/OAuth/ICloudConnectionService.cs
// PURPOSE:  The public application-facing surface for cloud connections.
//           The App depends on this interface only. It connects a provider
//           (authorize and durably store the refresh token), disconnects a
//           provider (revoke, then delete), and lists the instances that
//           have a stored credential under a provider id. It never exposes
//           the internal OAuth flow or token store.
// RELATION: Implemented by CloudConnectionService. Consumed by the C.8 UI.
//           Public because the App project references it across the
//           assembly boundary; the OAuth internals stay internal.
// ============================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Platform.Cloud.Credentials;

namespace FileHarvester.Platform.Cloud.OAuth;

/// <summary>
/// The application-facing cloud connection surface.
/// </summary>
/// <remarks>
/// <para>
/// The App depends on this interface only. The OAuth flow, the token store,
/// and the config store are internal to the Platform assembly.
/// </para>
/// <para>
/// Instances are enumerated per provider id, matching the credential
/// store surface. There is no "list all providers" operation: the store
/// enumerates instances under a profile id, and the design does not
/// fabricate a capability the store does not have.
/// </para>
/// </remarks>
public interface ICloudConnectionService
{
    /// <summary>
    /// Authorizes a provider and durably stores the refresh token for the
    /// given credential key. A first authorization always carries a refresh
    /// token; the operation throws when it does not.
    /// </summary>
    /// <param name="config">The non-secret client registration.</param>
    /// <param name="key">The credential key (provider id and instance).</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task ConnectAsync(
        OAuthClientConfig config,
        CredentialKey key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes the stored credential at the provider and deletes it from
    /// the credential store. The delete runs only after a successful
    /// revocation; if revocation fails, the stored token is preserved.
    /// </summary>
    /// <param name="config">The non-secret client registration.</param>
    /// <param name="key">The credential key (provider id and instance).</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task DisconnectAsync(
        OAuthClientConfig config,
        CredentialKey key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the instance names that have a stored credential under the
    /// given provider id. The credential values themselves are never
    /// returned.
    /// </summary>
    /// <param name="providerId">
    /// The provider id, such as <c>google-drive</c>. Non-null, non-empty.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The instance names. Never null; may be empty.
    /// </returns>
    Task<IReadOnlyList<string>> ListConnectedInstancesAsync(
        string providerId,
        CancellationToken cancellationToken = default);
}
