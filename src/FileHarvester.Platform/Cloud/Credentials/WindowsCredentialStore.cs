// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Credentials/WindowsCredentialStore.cs
// PURPOSE:  Windows Credential Manager implementation of ICredentialStore
//           and ITypedCredentialStore. Uses
//           Meziantou.Framework.Win32.CredentialManager to read, write,
//           and delete entries. The store key is the composed store key
//           from CredentialStoreKey.
// RELATION: Registered by AddFileHarvesterCloudCredentials() when the
//           host is Windows. The only working credential store in v1.1.0.
//           Implements both interfaces on the same instance so a static
//           credential and an OAuth token share the same underlying
//           credential manager without a second store object.
// ============================================================================

using System.Runtime.Versioning;
using Meziantou.Framework.Win32;

namespace FileHarvester.Platform.Cloud.Credentials;

/// <summary>
/// Windows Credential Manager credential storage.
/// </summary>
/// <remarks>
/// <para>
/// The constructor throws <see cref="PlatformNotSupportedException"/>
/// when the host is not Windows, so this type cannot be instantiated by
/// accident on another platform even if DI is misconfigured. The DI
/// factory never constructs it on non-Windows.
/// </para>
/// <para>
/// Credential values are written to Windows Credential Manager and
/// nowhere else. They are never logged. The store key is logged; the
/// value is not.
/// </para>
/// <para>
/// The static store-key format (file-ferry/{ProfileId}/{InstanceName})
/// is unchanged. OAuth keys use the reserved OAuth namespace
/// (file-ferry/oauth/{ProfileId}/{InstanceName}). The two namespaces
/// are enumerated separately by the listing methods.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialStore : ICredentialStore, ITypedCredentialStore
{
    /// <summary>
    /// Creates the store. Throws on non-Windows hosts.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">
    /// When the host is not Windows.
    /// </exception>
    public WindowsCredentialStore()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "WindowsCredentialStore is only supported on Windows.");
        }
    }

    // ------------------------------------------------------------------
    // ICredentialStore (static credentials)
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    [SupportedOSPlatform("windows5.1.2600")]
    public Task WriteAsync(
        CredentialKey key,
        string secret,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return WriteCoreAsync(
            CredentialStoreKey.FromCredentialKey(key), secret, cancellationToken);
    }

    /// <inheritdoc/>
    [SupportedOSPlatform("windows5.1.2600")]
    public Task<string?> ReadAsync(
        CredentialKey key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return ReadCoreAsync(
            CredentialStoreKey.FromCredentialKey(key), cancellationToken);
    }

    /// <inheritdoc/>
    [SupportedOSPlatform("windows5.1.2600")]
    public Task DeleteAsync(
        CredentialKey key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return DeleteCoreAsync(
            CredentialStoreKey.FromCredentialKey(key), cancellationToken);
    }

    /// <inheritdoc/>
    [SupportedOSPlatform("windows5.1.2600")]
    public Task<IReadOnlyList<string>> ListInstanceNamesAsync(
        string profileId,
        CancellationToken cancellationToken = default)
        => ListInstanceNamesAsync(profileId, CredentialKind.Static, cancellationToken);

    // ------------------------------------------------------------------
    // ITypedCredentialStore (typed keys, static or OAuth)
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    [SupportedOSPlatform("windows5.1.2600")]
    public Task WriteAsync(
        CredentialStoreKey key,
        string secret,
        CancellationToken cancellationToken = default)
        => WriteCoreAsync(key, secret, cancellationToken);

    /// <inheritdoc/>
    [SupportedOSPlatform("windows5.1.2600")]
    public Task<string?> ReadAsync(
        CredentialStoreKey key,
        CancellationToken cancellationToken = default)
        => ReadCoreAsync(key, cancellationToken);

    /// <inheritdoc/>
    [SupportedOSPlatform("windows5.1.2600")]
    public Task DeleteAsync(
        CredentialStoreKey key,
        CancellationToken cancellationToken = default)
        => DeleteCoreAsync(key, cancellationToken);

    /// <inheritdoc/>
    [SupportedOSPlatform("windows5.1.2600")]
    public Task<IReadOnlyList<string>> ListInstanceNamesAsync(
        string profileId,
        CredentialKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        cancellationToken.ThrowIfCancellationRequested();

        var prefix = BuildPrefix(profileId, kind);
        var matches = CredentialManager.EnumerateCredentials();
        var results = new List<string>();

        foreach (var credential in matches)
        {
            var name = credential.ApplicationName;
            if (name is not null && name.StartsWith(prefix, StringComparison.Ordinal))
            {
                var instanceName = name.Substring(prefix.Length);
                if (instanceName.Length > 0)
                {
                    results.Add(instanceName);
                }
            }
        }

        return Task.FromResult<IReadOnlyList<string>>(results);
    }

    // ------------------------------------------------------------------
    // Shared implementation. One source of truth for the store key and
    // the namespace prefix, so the static and OAuth paths cannot drift.
    // ------------------------------------------------------------------

    [SupportedOSPlatform("windows5.1.2600")]
    private static Task WriteCoreAsync(
        CredentialStoreKey key,
        string secret,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secret);
        cancellationToken.ThrowIfCancellationRequested();

        var storeKey = key.ToStoreKey();
        CredentialManager.WriteCredential(
            applicationName: storeKey,
            userName: key.ProfileId,
            secret: secret,
            persistence: CredentialPersistence.LocalMachine);

        return Task.CompletedTask;
    }

    [SupportedOSPlatform("windows5.1.2600")]
    private static Task<string?> ReadCoreAsync(
        CredentialStoreKey key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var storeKey = key.ToStoreKey();
        var credential = CredentialManager.ReadCredential(storeKey);
        var value = credential?.Password;

        return Task.FromResult(value);
    }

    [SupportedOSPlatform("windows5.1.2600")]
    private static Task DeleteCoreAsync(
        CredentialStoreKey key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var storeKey = key.ToStoreKey();
        CredentialManager.DeleteCredential(storeKey);

        return Task.CompletedTask;
    }

    private static string BuildPrefix(string profileId, CredentialKind kind)
    {
        var prefix = "file-ferry" + (char)47;
        if (kind == CredentialKind.OAuth)
        {
            prefix += "oauth" + (char)47;
        }
        return prefix + profileId + (char)47;
    }
}
