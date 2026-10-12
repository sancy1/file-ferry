// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Credentials/ICredentialStore.cs
// PURPOSE:  Contract for platform credential storage. Windows uses
//           Windows Credential Manager. macOS and Linux throw a
//           named CloudCredentialUnavailableException until a later
//           platform phase delivers their backends.
// RELATION: Consumed by every cloud provider that requires a stored
//           credential. Registered in AddFileHarvester via a factory.
//           Nothing here is Core; credential persistence is a
//           platform concern.
// ============================================================================

namespace FileHarvester.Platform.Cloud.Credentials;

/// <summary>
/// Platform credential storage: write, read, delete, and list
/// instance names for one provider.
/// </summary>
/// <remarks>
/// <para>
/// The value stored by <see cref="WriteAsync"/> is never returned
/// anywhere except by <see cref="ReadAsync"/>. It is never written
/// to a log, a JSON file, an exception message, or a test output.
/// </para>
/// <para>
/// On a platform whose backend is not implemented in this release,
/// every method throws <see cref="CloudCredentialUnavailableException"/>
/// with a specific message. The exception is not a stub; it is the
/// declared boundary of this release.
/// </para>
/// </remarks>
public interface ICredentialStore
{
    /// <summary>
    /// Stores the credential value for the given key. Overwrites any
    /// existing value for the same key.
    /// </summary>
    /// <param name="key">The credential key.</param>
    /// <param name="secret">
    /// The credential value. Never null; may be empty.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task WriteAsync(
        CredentialKey key,
        string secret,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the credential value for the given key.
    /// </summary>
    /// <param name="key">The credential key.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The stored value, or null when no value is stored.</returns>
    Task<string?> ReadAsync(
        CredentialKey key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the credential for the given key. No-op when no value
    /// is stored.
    /// </summary>
    /// <param name="key">The credential key.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task DeleteAsync(
        CredentialKey key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the instance names stored under a provider profile ID.
    /// </summary>
    /// <param name="profileId">The provider profile ID, such as <c>aws-s3</c>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The instance names. Never null; may be empty. The credential
    /// values themselves are never returned here.
    /// </returns>
    Task<IReadOnlyList<string>> ListInstanceNamesAsync(
        string profileId,
        CancellationToken cancellationToken = default);
}
