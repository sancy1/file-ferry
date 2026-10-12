// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Credentials/NotSupportedCredentialStore.cs
// PURPOSE:  Defensive fallback for an unrecognized platform. Every
//           method of both interfaces throws
//           CloudCredentialUnavailableException.
// RELATION: Registered by AddFileHarvesterCloudCredentials() when none
//           of Windows, macOS, or Linux is detected. Implements
//           ICredentialStore and ITypedCredentialStore. This should not
//           happen in practice; it is a fail-loud backstop.
// ============================================================================

namespace FileHarvester.Platform.Cloud.Credentials;

/// <summary>
/// Credential storage for an unrecognized platform. Every method of
/// both interfaces throws.
/// </summary>
public sealed class NotSupportedCredentialStore : ICredentialStore, ITypedCredentialStore
{
    private const string PlatformName = "this platform";
    private const string RequiredBackend = "a supported credential backend";
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
