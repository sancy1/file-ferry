// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/OAuth/OAuthClientConfigStore.cs
// PURPOSE:  JSON-backed implementation of IOAuthClientConfigStore. Reads,
//           upserts, and deletes non-secret OAuth client registrations in
//           a file named oauth-clients.json inside an injected storage
//           directory. Writes are crash-resistant within the filesystem's
//           guarantees: content is written to a temporary file in the same
//           directory and then moved into place.
// RELATION: Implements IOAuthClientConfigStore. The store does not compute
//           %APPDATA%; the composition root supplies the directory.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Core.Composition;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.Platform.Cloud.OAuth;

/// <summary>
/// JSON-backed implementation of <see cref="IOAuthClientConfigStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// A missing or malformed file is treated as empty; the caller is not told,
/// matching the provider-profile store precedent. A missing file means no
/// registrations yet; a corrupt file is recoverable by re-registering.
/// </para>
/// <para>
/// A write is crash-resistant within the filesystem's guarantees, not a
/// universal transactional guarantee: the complete JSON is written to a
/// temporary file in the same directory, flushed and closed, then moved
/// over the destination. If writing or replacement fails, the previous
/// file is preserved and the temporary file is removed.
/// </para>
/// <para>
/// Every file and directory operation runs through the resilience executor
/// when one is supplied. When null, operations run unwrapped; this is the
/// deterministic test path.
/// </para>
/// </remarks>
public sealed class OAuthClientConfigStore : IOAuthClientConfigStore
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
    /// <c>oauth-clients.json</c> inside that directory.
    /// </summary>
    /// <param name="storageDirectory">
    /// The directory. Usually <c>%APPDATA%\FileHarvester</c>. Supplied by the
    /// composition root; the store does not compute it.
    /// </param>
    /// <param name="resilience">
    /// Optional resilience executor. When supplied, every file and directory
    /// operation runs through it. When null, operations run directly; this is
    /// the deterministic test path.
    /// </param>
    public OAuthClientConfigStore(string storageDirectory, IResilienceExecutor? resilience = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);
        _filePath = Path.Combine(storageDirectory, "oauth-clients.json");
        _resilience = resilience;
    }

    /// <inheritdoc />
    public async Task<OAuthClientConfig?> ReadAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        cancellationToken.ThrowIfCancellationRequested();

        var list = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        foreach (var config in list)
        {
            if (string.Equals(config.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            {
                return config;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Reading.
    // ------------------------------------------------------------------

    /// <summary>
    /// Reads every registration. A missing or malformed file yields an empty
    /// list; the caller is not told.
    /// </summary>
    private async Task<IReadOnlyList<OAuthClientConfig>> ReadAllAsync(CancellationToken cancellationToken)
    {
        var exists = await FileExistsAsync(cancellationToken).ConfigureAwait(false);
        if (!exists)
        {
            return Array.Empty<OAuthClientConfig>();
        }

        try
        {
            var json = await ReadAllTextAsync(cancellationToken).ConfigureAwait(false);
            var list = JsonSerializer.Deserialize<List<OAuthClientConfig>>(json, JsonOptions);
            return list ?? (IReadOnlyList<OAuthClientConfig>)Array.Empty<OAuthClientConfig>();
        }
        catch (JsonException)
        {
            return Array.Empty<OAuthClientConfig>();
        }
    }

    /// <inheritdoc />
    public async Task UpsertAsync(
        OAuthClientConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        Validate(config);
        cancellationToken.ThrowIfCancellationRequested();

        var existing = new List<OAuthClientConfig>(await ReadAllAsync(cancellationToken)
            .ConfigureAwait(false));

        var idx = existing.FindIndex(c =>
            string.Equals(c.ProviderId, config.ProviderId, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            existing[idx] = config;
        }
        else
        {
            existing.Add(config);
        }

        await WriteAllAtomicAsync(existing, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        cancellationToken.ThrowIfCancellationRequested();

        var existing = new List<OAuthClientConfig>(await ReadAllAsync(cancellationToken)
            .ConfigureAwait(false));

        var removed = existing.RemoveAll(c =>
            string.Equals(c.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));

        if (removed == 0)
        {
            return;
        }

        await WriteAllAtomicAsync(existing, cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // Validation.
    // ------------------------------------------------------------------

    /// <summary>
    /// Validates the registration before any write. Throws ArgumentException
    /// naming the offending field. A client secret cannot be present: the
    /// record has no such field.
    /// </summary>
    private static void Validate(OAuthClientConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ProviderId))
        {
            throw new ArgumentException("ProviderId is required.", nameof(config));
        }
        if (string.IsNullOrWhiteSpace(config.ClientId))
        {
            throw new ArgumentException("ClientId is required.", nameof(config));
        }
        RequireAbsoluteUri(config.AuthorizationEndpoint, "AuthorizationEndpoint");
        RequireAbsoluteUri(config.TokenEndpoint, "TokenEndpoint");
        RequireAbsoluteUri(config.RedirectUri, "RedirectUri");
        if (config.Scopes is null || config.Scopes.Count == 0)
        {
            throw new ArgumentException("Scopes must contain at least one scope.", nameof(config));
        }
    }

    private static void RequireAbsoluteUri(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            throw new ArgumentException(fieldName + " must be an absolute URI.", fieldName);
        }
    }

    // ------------------------------------------------------------------
    // Atomic write.
    // ------------------------------------------------------------------

    /// <summary>
    /// Writes the full list to a temporary file in the destination
    /// directory, then moves it over the destination. Crash-resistant within
    /// the filesystem's guarantees, not a universal transactional guarantee.
    /// The previous file is preserved if the write or replacement fails.
    /// </summary>
    private async Task WriteAllAtomicAsync(
        List<OAuthClientConfig> configs,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(configs, JsonOptions);
        var directory = Path.GetDirectoryName(_filePath) ?? string.Empty;
        var tempPath = Path.Combine(directory,
            "oauth-clients.json.tmp-" + Guid.NewGuid().ToString("N"));

        try
        {
            if (_resilience is null)
            {
                EnsureDirectory();
                await WriteAndReplaceAsync(tempPath, json, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await _resilience.ExecuteAsync(
                policyName: PolicyNames.BundleWrite,
                operation: async token => await WriteAndReplaceAsync(tempPath, json, token)
                    .ConfigureAwait(false),
                ct: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch (IOException) { }
            }
        }
    }

    /// <summary>
    /// Writes the temporary file, flushes and closes it, then moves it over
    /// the destination.
    /// </summary>
    private async Task WriteAndReplaceAsync(
        string tempPath,
        string json,
        CancellationToken cancellationToken)
    {
        await using (var stream = new FileStream(
            tempPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        File.Move(tempPath, _filePath, overwrite: true);
    }

    // ------------------------------------------------------------------
    // Wrapped file I/O.
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

    private void EnsureDirectory()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}
