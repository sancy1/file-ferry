// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Credentials/MacOsKeychainStore.cs
// PURPOSE:  Placeholder for the macOS Keychain credential backend.
//           Not implemented in v1.1.0. Every method of both interfaces
//           throws CloudCredentialUnavailableException with a specific
//           message naming macOS and the scheduled phase.
// RELATION: Registered by AddFileHarvesterCloudCredentials() when the
//           host is macOS. Implements ICredentialStore and
//           ITypedCredentialStore. Replaced with a real implementation
//           in the Platform Enablement phase.
// ============================================================================

namespace FileHarvester.Platform.Cloud.Credentials;

/// <summary>
/// The macOS Keychain credential backend. Not implemented in v1.1.0.
/// </summary>
/// <remarks>
/// <para>
/// Every method of both interfaces throws
/// <see cref="CloudCredentialUnavailableException"/>. The message names
/// macOS, the required backend (macOS Keychain), and the scheduled
/// delivery phase. Local file features are unaffected and continue to
/// work on macOS.
/// </para>
/// </remarks>
public sealed class MacOsKeychainStore : ICredentialStore, ITypedCredentialStore
{
    private const string PlatformName = "macOS";
    private const string RequiredBackend = "macOS Keychain";
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
