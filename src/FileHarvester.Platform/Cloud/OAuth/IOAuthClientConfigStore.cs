// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/OAuth/IOAuthClientConfigStore.cs
// PURPOSE:  Read, upsert, and delete the non-secret OAuth client
//           registrations persisted for the OAuth provider family. A
//           registration carries the public client id, the authorization
//           and token endpoints, the scopes, and the loopback redirect URI.
//           It never carries a client secret: desktop OAuth uses PKCE.
// RELATION: Implemented by OAuthClientConfigStore. Consumed by the C.8
//           registration UI and by the OAuth flows that resolve a provider
//           configuration at run time.
// ============================================================================

using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.OAuth;

/// <summary>
/// The read, upsert, and delete surface for OAuth client registrations.
/// </summary>
/// <remarks>
/// <para>
/// A registration is keyed by provider id (for example <c>google-drive</c>).
/// Provider id matching is case-insensitive. An upsert replaces an existing
/// registration for the same provider id, so a duplicate can never be
/// created through this surface.
/// </para>
/// <para>
/// No method reads or writes a client secret. The registration record has
/// no secret field; the store validates the record shape before writing.
/// </para>
/// </remarks>
public interface IOAuthClientConfigStore
{
    /// <summary>
    /// Reads the registration for a provider id.
    /// </summary>
    /// <param name="providerId">
    /// The provider id. Non-null, non-empty.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The registration, or null when the store file is missing, malformed,
    /// or carries no registration for the provider id. A missing or
    /// malformed file is treated as empty; the caller is not told.
    /// </returns>
    Task<OAuthClientConfig?> ReadAsync(
        string providerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or replaces the registration for a provider id. Validates the
    /// configuration before writing. The write is crash-resistant within the
    /// filesystem's guarantees: the new content is written to a temporary
    /// file in the same directory and then moved into place, preserving the
    /// previous file if the write or replacement fails.
    /// </summary>
    /// <param name="config">
    /// The registration. Non-null. Provider id, client id, both endpoints,
    /// the redirect URI, and the scopes must be valid.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="System.ArgumentException">
    /// The provider id, client id, an endpoint, the redirect URI, or the
    /// scopes are missing or invalid.
    /// </exception>
    Task UpsertAsync(
        OAuthClientConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the registration for a provider id. A no-op when the store
    /// file is missing or carries no registration for that provider id.
    /// </summary>
    /// <param name="providerId">
    /// The provider id. Non-null, non-empty.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task DeleteAsync(
        string providerId,
        CancellationToken cancellationToken = default);
}
