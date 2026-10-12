// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Credentials/LinuxSecretServiceStore.cs
// PURPOSE:  Placeholder for the Linux Secret Service credential
//           backend. Not implemented in v1.1.0. Every method of both
//           interfaces throws CloudCredentialUnavailableException with a
//           specific message naming Linux and the scheduled phase.
// RELATION: Registered by AddFileHarvesterCloudCredentials() when the
//           host is Linux. Implements ICredentialStore and
//           ITypedCredentialStore. Replaced with a real implementation
//           in the Platform Enablement phase.
// ============================================================================

namespace FileHarvester.Platform.Cloud.Credentials;

/// <summary>
/// The Linux Secret Service credential backend. Not implemented in
/// v1.1.0.
/// </summary>
/// <remarks>
/// <para>
/// Every method of both interfaces throws
/// <see cref="CloudCredentialUnavailableException"/>. The message names
/// Linux, the required backend (Linux Secret Service, also called
/// libsecret), and the scheduled delivery phase. Local file features
/// are unaffected.
/// </para>
/// </remarks>
public sealed class LinuxSecretServiceStore : ICredentialStore, ITypedCredentialStore
{
    private const string PlatformName = "Linux";
    private const string RequiredBackend = "Linux Secret Service (libsecret)";
    private const string ScheduledPhase = "Platform Enablement";

    private static CloudCredentialUnavailableException NotAvailable() =>
        new(PlatformName, RequiredBackend, ScheduledPhase);

    // ------------------------------------------------------------------
    // ICredentialStore
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    public Task WriteAsync(
        CredentialKey key,
        string secret,
        CancellationToken cancellationToken = default) =>
        throw NotAvailable();

    /// <inheritdoc/>
    public Task<string?> ReadAsync(
        CredentialKey key,
        CancellationToken cancellationToken = default) =>
        throw NotAvailable();

    /// <inheritdoc/>
    public Task DeleteAsync(
        CredentialKey key,
        CancellationToken cancellationToken = default) =>
        throw NotAvailable();

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ListInstanceNamesAsync(
        string profileId,
        CancellationToken cancellationToken = default) =>
        throw NotAvailable();

    // ------------------------------------------------------------------
    // ITypedCredentialStore
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    public Task WriteAsync(
        CredentialStoreKey key,
        string secret,
        CancellationToken cancellationToken = default) =>
        throw NotAvailable();

    /// <inheritdoc/>
    public Task<string?> ReadAsync(
        CredentialStoreKey key,
        CancellationToken cancellationToken = default) =>
        throw NotAvailable();

    /// <inheritdoc/>
    public Task DeleteAsync(
        CredentialStoreKey key,
        CancellationToken cancellationToken = default) =>
        throw NotAvailable();

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ListInstanceNamesAsync(
        string profileId,
        CredentialKind kind,
        CancellationToken cancellationToken = default) =>
        throw NotAvailable();
}
