// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Profiles/ProviderProfileStore.cs
// PURPOSE:  Persists user-created custom provider profiles to a JSON
//           file under %APPDATA%\FileHarvester. Never stores credential
//           values: only the profile schema (Id, DisplayName, Kind,
//           AuthKind, CredentialFields as field specs, endpoint URL,
//           prompt template, icon key).
// RELATION: Consumed by the C.8 Cloud Connections dialog. Built-in
//           profiles come from Core's BuiltInProfiles; this store is
//           only for custom profiles the user defines.
//           File I/O is wrapped in the file-read and bundle-write
//           resilience policies when an executor is provided.
// ============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using FileHarvester.Core.Composition;
using FileHarvester.Core.Domain;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.Platform.Cloud.Profiles;

/// <summary>
/// Reads and writes custom provider profiles.
/// </summary>
public interface IProviderProfileStore
{
    /// <summary>
    /// Lists all custom profiles. Returns an empty list when the store
    /// file is missing or malformed; never throws for a missing or
    /// malformed file.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The custom profiles. Never null; may be empty.</returns>
    Task<IReadOnlyList<ProviderProfile>> ListCustomAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a custom profile. Overwrites any existing profile with the
    /// same Id.
    /// </summary>
    /// <param name="profile">The profile to save. Never null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task SaveCustomAsync(
        ProviderProfile profile,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a custom profile by Id. No-op when the Id does not exist.
    /// </summary>
    /// <param name="profileId">The profile Id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task DeleteCustomAsync(
        string profileId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// JSON-backed implementation of <see cref="IProviderProfileStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// The file is user-editable. A malformed file is treated as empty; the
/// caller is not told, because blocking the app on a corrupt config is
/// worse than starting fresh. A missing file is treated as empty.
/// </para>
/// <para>
/// Credential values are never stored here. Only the profile schema.
/// Credential values are stored exclusively by the platform
/// <see cref="FileHarvester.Platform.Cloud.Credentials.ICredentialStore"/>
/// in Windows Credential Manager on Windows.
/// </para>
/// <para>
/// Every file and directory operation runs through the resilience
/// executor when one is supplied. When null, operations run unwrapped;
/// this is the test path and the fallback for a DI graph that has not
/// yet registered the store.
/// </para>
/// </remarks>
public sealed class ProviderProfileStore : IProviderProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;
    private readonly IResilienceExecutor? _resilience;

    /// <summary>
    /// Creates a store over the given directory. The file is named
    /// <c>cloud-profiles.json</c> inside that directory.
    /// </summary>
    /// <param name="storageDirectory">
    /// The directory. Usually <c>%APPDATA%\FileHarvester</c>.
    /// </param>
    /// <param name="resilience">
    /// Optional resilience executor. When supplied, every file and
    /// directory operation runs through it. When null, operations run
    /// directly; this is the test path.
    /// </param>
    public ProviderProfileStore(string storageDirectory, IResilienceExecutor? resilience = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);
        _filePath = Path.Combine(storageDirectory, "cloud-profiles.json");
        _resilience = resilience;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProviderProfile>> ListCustomAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var exists = await FileExistsAsync(cancellationToken).ConfigureAwait(false);
        if (!exists)
        {
            return Array.Empty<ProviderProfile>();
        }

        try
        {
            var json = await ReadAllTextAsync(cancellationToken).ConfigureAwait(false);

            var list = JsonSerializer.Deserialize<List<ProviderProfile>>(json, JsonOptions);
            return list ?? (IReadOnlyList<ProviderProfile>)Array.Empty<ProviderProfile>();
        }
        catch (JsonException)
        {
            return Array.Empty<ProviderProfile>();
        }
    }

    /// <inheritdoc/>
    public async Task SaveCustomAsync(
        ProviderProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();

        var existing = new List<ProviderProfile>(await ListCustomAsync(cancellationToken)
            .ConfigureAwait(false));

        var idx = existing.FindIndex(p =>
            string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            existing[idx] = profile;
        }
        else
        {
            existing.Add(profile);
        }

        await WriteAllAsync(existing, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteCustomAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        cancellationToken.ThrowIfCancellationRequested();

        var existing = new List<ProviderProfile>(await ListCustomAsync(cancellationToken)
            .ConfigureAwait(false));

        var removed = existing.RemoveAll(p =>
            string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));

        if (removed == 0)
        {
            return;
        }

        await WriteAllAsync(existing, cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // Wrapped file I/O
    // ------------------------------------------------------------------

    private async Task<bool> FileExistsAsync(CancellationToken cancellationToken)
    {
        if (_resilience is null)
        {
            return File.Exists(_filePath);
        }

        return await _resilience.ExecuteAsync(
            policyName: PolicyNames.FileRead,
            operation: _ => Task.FromResult(File.Exists(_filePath)),
            fallback: _ => Task.FromResult(false),
            ct: cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ReadAllTextAsync(CancellationToken cancellationToken)
    {
        if (_resilience is null)
        {
            return await File.ReadAllTextAsync(_filePath, cancellationToken)
                .ConfigureAwait(false);
        }

        return await _resilience.ExecuteAsync(
            policyName: PolicyNames.FileRead,
            operation: async token => await File.ReadAllTextAsync(_filePath, token)
                .ConfigureAwait(false),
            fallback: _ => Task.FromResult(string.Empty),
            ct: cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteAllAsync(
        List<ProviderProfile> profiles,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(profiles, JsonOptions);

        if (_resilience is null)
        {
            EnsureDirectory();
            await File.WriteAllTextAsync(_filePath, json, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await _resilience.ExecuteAsync(
            policyName: PolicyNames.BundleWrite,
            operation: async token =>
            {
                EnsureDirectory();
                await File.WriteAllTextAsync(_filePath, json, token)
                    .ConfigureAwait(false);
            },
            ct: cancellationToken).ConfigureAwait(false);
    }

    private void EnsureDirectory()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}
