// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Credentials/ITypedCredentialStore.cs
// PURPOSE:  The kind-aware credential-store surface. Static credentials and
//           OAuth refresh tokens use distinct namespaces; this interface
//           lets a caller address one kind without affecting the other.
//           It is separate from ICredentialStore so no existing implementer
//           is forced to change. Implementers that support typed keys
//           implement both interfaces on the same concrete store instance.
// RELATION: Implemented by the concrete platform stores alongside
//           ICredentialStore. Consumed by OAuthTokenStore and by
//           CloudConnectionService for OAuth listing. The legacy
//           ICredentialStore contract remains the static-credential
//           surface and is unchanged.
// ============================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.Credentials;

/// <summary>
/// Platform credential storage addressed by a typed store key.
/// </summary>
/// <remarks>
/// <para>
/// The value stored by <see cref="WriteAsync"/> is never returned
/// anywhere except by <see cref="ReadAsync"/>. It is never written
/// to a log, a JSON file, an exception message, or a test output.
/// </para>
/// <para>
/// A static key and an OAuth key for the same profile and instance
/// are distinct entries. The listing method returns only the names
/// whose kind matches the requested kind; a static credential cannot
/// appear in an OAuth listing and vice versa.
/// </para>
/// </remarks>
public interface ITypedCredentialStore
{
    /// <summary>
    /// Stores the credential value for the given typed key. Overwrites
    /// any existing value for the same key.
    /// </summary>
    /// <param name="key">The typed credential key.</param>
    /// <param name="secret">
    /// The credential value. Never null; may be empty.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task WriteAsync(
        CredentialStoreKey key,
        string secret,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the credential value for the given typed key.
    /// </summary>
    /// <param name="key">The typed credential key.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored value, or null when no value is stored.</returns>
    Task<string?> ReadAsync(
        CredentialStoreKey key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the credential for the given typed key. No-op when no
    /// value is stored.
    /// </summary>
    /// <param name="key">The typed credential key.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task DeleteAsync(
        CredentialStoreKey key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the instance names of the requested kind stored under a
    /// provider profile ID. Only the kind's namespace is enumerated;
    /// credentials of the other kind are not returned.
    /// </summary>
    /// <param name="profileId">
    /// The provider profile ID, such as <c>aws-s3</c>.
    /// </param>
    /// <param name="kind">The credential kind to enumerate.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The instance names of the requested kind. Never null; may be
    /// empty. The credential values themselves are never returned here.
    /// </returns>
    Task<IReadOnlyList<string>> ListInstanceNamesAsync(
        string profileId,
        CredentialKind kind,
        CancellationToken cancellationToken = default);
}
