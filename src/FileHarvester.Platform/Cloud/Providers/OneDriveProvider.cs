// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/OneDriveProvider.cs
// PURPOSE:  ICloudProvider implementation for Microsoft OneDrive. Mirrors
//           GoogleDriveProvider in shape: the same constructor, the same
//           resilience wrapping with named arguments, the same no-fallback
//           policy. OneDrive has no bucket or key: items are identified by
//           item id in an onedrive://<item-id> URI.
// RELATION: Implements ICloudProvider. Consumes IOneDriveClient and
//           IResilienceExecutor. Internal because IOneDriveClient is
//           internal. Instance selection is performed by
//           ProviderConstruction.CreateOneDrive, not here.
// NOTES
//   ParseOneDrivePath uses PathScheme.Parse, so both onedrive://item-id
//   and onedrive://[instance]/item-id yield the item id correctly. This
//   is the correct form; GoogleDriveProvider.ParseGDrivePath has the
//   latent defect recorded as F-135.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Core.Composition;
using FileHarvester.Core.Domain;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// OneDrive implementation of <see cref="ICloudProvider"/>.
/// </summary>
internal sealed class OneDriveProvider : ICloudProvider
{
    private const string OneDriveScheme = "onedrive://";

    private readonly IOneDriveClient _client;
    private readonly IResilienceExecutor _resilience;

    /// <summary>
    /// Creates a provider over the OneDrive wrapper.
    /// </summary>
    /// <param name="client">The wrapper over Graph. Not null.</param>
    /// <param name="resilience">The resilience executor. Not null.</param>
    public OneDriveProvider(
        IOneDriveClient client,
        IResilienceExecutor resilience)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _resilience = resilience ?? throw new ArgumentNullException(nameof(resilience));
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadAsync(
        string cloudPath,
        CancellationToken ct = default)
    {
        var itemId = ParseOneDrivePath(cloudPath);

        return await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudRead,
            operation: async token => await _client
                .DownloadAsync(itemId, token)
                .ConfigureAwait(false),
            idempotencyKey: "onedrive-read-" + itemId,
            timeBudgetMs: 30_000,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CloudEntryPage> ListAsync(
        string prefix,
        string? continuationToken,
        int maxEntries,
        CancellationToken ct = default)
    {
        var folderId = ParseOneDrivePath(prefix);

        var page = await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudSearch,
            operation: async token => await _client
                .ListAsync(folderId, continuationToken, maxEntries, token)
                .ConfigureAwait(false),
            idempotencyKey: "onedrive-list-" + folderId,
            timeBudgetMs: 60_000,
            ct: ct).ConfigureAwait(false);

        var entries = new List<CloudEntry>(page.Items.Count);
        foreach (var item in page.Items)
        {
            entries.Add(new CloudEntry(
                Path: OneDriveScheme + item.Id,
                Name: item.Name ?? item.Id,
                IsDirectory: item.IsFolder,
                SizeBytes: item.SizeBytes,
                LastModifiedUtc: item.LastModifiedUtc,
                ContentType: item.MimeType));
        }

        return new CloudEntryPage(entries, page.ContinuationToken);
    }

    /// <inheritdoc />
    public Task<CloudWriteHandle> OpenWriteAsync(
        string cloudPath,
        string? contentType,
        CancellationToken ct = default)
    {
        var itemId = ParseOneDrivePath(cloudPath);

        var buffer = new MemoryStream();

        Func<CancellationToken, Task> complete = async token =>
        {
            buffer.Position = 0;

            await _resilience.ExecuteAsync(
                policyName: PolicyNames.CloudWrite,
                operation: async innerToken => await _client
                    .UpdateAsync(itemId, contentType, buffer, innerToken)
                    .ConfigureAwait(false),
                idempotencyKey: "onedrive-write-" + itemId,
                timeBudgetMs: 300_000,
                ct: token).ConfigureAwait(false);
        };

        return Task.FromResult(new CloudWriteHandle(buffer, complete));
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        string cloudPath,
        CancellationToken ct = default)
    {
        var itemId = ParseOneDrivePath(cloudPath);

        return await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudRead,
            operation: async token => await _client
                .ExistsAsync(itemId, token)
                .ConfigureAwait(false),
            idempotencyKey: "onedrive-exists-" + itemId,
            timeBudgetMs: 30_000,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        string cloudPath,
        CancellationToken ct = default)
    {
        var itemId = ParseOneDrivePath(cloudPath);

        await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudWrite,
            operation: async token => await _client
                .DeleteIfExistsAsync(itemId, token)
                .ConfigureAwait(false),
            idempotencyKey: "onedrive-delete-" + itemId,
            timeBudgetMs: 30_000,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ProviderMetadata> GetMetadataAsync(
        string cloudPath,
        CancellationToken ct = default)
    {
        var itemId = ParseOneDrivePath(cloudPath);

        var meta = await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudRead,
            operation: async token => await _client
                .GetPropertiesAsync(itemId, token)
                .ConfigureAwait(false),
            idempotencyKey: "onedrive-meta-" + itemId,
            timeBudgetMs: 30_000,
            ct: ct).ConfigureAwait(false);

        var extra = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["etag"] = meta.ETag ?? string.Empty,
            ["is-folder"] = meta.IsFolder ? "true" : "false",
        };

        return new ProviderMetadata(
            SizeBytes: meta.SizeBytes,
            LastModifiedUtc: meta.LastModifiedUtc,
            ContentType: meta.MimeType,
            ETag: meta.ETag,
            Extra: extra);
    }

    // ------------------------------------------------------------------
    // Path parsing.
    // ------------------------------------------------------------------

    /// <summary>
    /// Parses an onedrive://&lt;item-id&gt; URI into the item id. Uses
    /// PathScheme.Parse so the optional [instance] segment is stripped
    /// correctly. Rejects a non-onedrive scheme or an empty id.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The path is not an onedrive:// path, or the item id is empty.
    /// </exception>
    private static string ParseOneDrivePath(string cloudPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cloudPath);

        var info = PathScheme.Parse(cloudPath);

        if (info.Kind != PathSchemeKind.OneDrive)
        {
            throw new ArgumentException(
                "Path does not start with onedrive://",
                nameof(cloudPath));
        }

        if (string.IsNullOrEmpty(info.Rest))
        {
            throw new ArgumentException(
                "onedrive:// path has no item id.",
                nameof(cloudPath));
        }

        return info.Rest;
    }
}
