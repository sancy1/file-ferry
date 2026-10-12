// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/DropboxProvider.cs
// PURPOSE:  ICloudProvider implementation for Dropbox. Mirrors
//           OneDriveProvider in shape: the same constructor, the same
//           resilience wrapping with named arguments, the same no-fallback
//           policy. Dropbox is path-oriented, unlike Drive and OneDrive
//           which are id-oriented: objects are identified by a
//           root-relative Dropbox path inside a dropbox:// URI.
// RELATION: Implements ICloudProvider. Consumes IDropboxClient and
//           IResilienceExecutor. Internal because IDropboxClient is
//           internal. Instance selection is performed by
//           ProviderConstruction.CreateDropbox, not here.
// NOTES
//   ParseDropboxPath uses PathScheme.Parse, so dropbox:///folder/file.txt,
//   dropbox://folder/file.txt, and dropbox://[instance]/folder/file.txt
//   all yield a rooted Dropbox path. A missing leading slash is added;
//   the rest is passed to the client unchanged. There is no URL decoding.
//   Dropbox has no content-type field on its metadata; ContentType is null.
//   The Dropbox Rev is surfaced as the ETag - it is the closest analogue
//   to a concurrency token the SDK exposes.
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
/// Dropbox implementation of <see cref="ICloudProvider"/>.
/// </summary>
internal sealed class DropboxProvider : ICloudProvider
{
    private const string DropboxScheme = "dropbox://";

    private readonly IDropboxClient _client;
    private readonly IResilienceExecutor _resilience;

    /// <summary>
    /// Creates a provider over the Dropbox wrapper.
    /// </summary>
    /// <param name="client">The wrapper over the Dropbox SDK. Not null.</param>
    /// <param name="resilience">The resilience executor. Not null.</param>
    public DropboxProvider(
        IDropboxClient client,
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
        var path = ParseDropboxPath(cloudPath);

        return await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudRead,
            operation: async token => await _client
                .DownloadAsync(path, token)
                .ConfigureAwait(false),
            idempotencyKey: "dropbox-read-" + path,
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
        var path = ParseDropboxPath(prefix);

        var page = await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudSearch,
            operation: async token => await _client
                .ListAsync(path, continuationToken, maxEntries, token)
                .ConfigureAwait(false),
            idempotencyKey: "dropbox-list-" + path,
            timeBudgetMs: 60_000,
            ct: ct).ConfigureAwait(false);

        var entries = new List<CloudEntry>(page.Items.Count);
        foreach (var item in page.Items)
        {
            entries.Add(new CloudEntry(
                Path: DropboxScheme + item.PathLower,
                Name: item.Name,
                IsDirectory: item.IsFolder,
                SizeBytes: item.SizeBytes,
                LastModifiedUtc: item.ServerModifiedUtc,
                ContentType: null));
        }

        return new CloudEntryPage(entries, page.ContinuationToken);
    }

    /// <inheritdoc />
    public Task<CloudWriteHandle> OpenWriteAsync(
        string cloudPath,
        string? contentType,
        CancellationToken ct = default)
    {
        var path = ParseDropboxPath(cloudPath);

        var buffer = new MemoryStream();

        Func<CancellationToken, Task> complete = async token =>
        {
            buffer.Position = 0;

            await _resilience.ExecuteAsync(
                policyName: PolicyNames.CloudWrite,
                operation: async innerToken => await _client
                    .UpdateAsync(path, contentType, buffer, innerToken)
                    .ConfigureAwait(false),
                idempotencyKey: "dropbox-write-" + path,
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
        var path = ParseDropboxPath(cloudPath);

        return await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudRead,
            operation: async token => await _client
                .ExistsAsync(path, token)
                .ConfigureAwait(false),
            idempotencyKey: "dropbox-exists-" + path,
            timeBudgetMs: 30_000,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        string cloudPath,
        CancellationToken ct = default)
    {
        var path = ParseDropboxPath(cloudPath);

        await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudWrite,
            operation: async token => await _client
                .DeleteIfExistsAsync(path, token)
                .ConfigureAwait(false),
            idempotencyKey: "dropbox-delete-" + path,
            timeBudgetMs: 30_000,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ProviderMetadata> GetMetadataAsync(
        string cloudPath,
        CancellationToken ct = default)
    {
        var path = ParseDropboxPath(cloudPath);

        var meta = await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudRead,
            operation: async token => await _client
                .GetPropertiesAsync(path, token)
                .ConfigureAwait(false),
            idempotencyKey: "dropbox-meta-" + path,
            timeBudgetMs: 30_000,
            ct: ct).ConfigureAwait(false);

        var extra = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["rev"] = meta.Rev ?? string.Empty,
            ["content-hash"] = meta.ContentHash ?? string.Empty,
            ["is-folder"] = meta.IsFolder ? "true" : "false",
        };

        return new ProviderMetadata(
            SizeBytes: meta.SizeBytes,
            LastModifiedUtc: meta.ServerModifiedUtc,
            ContentType: null,
            ETag: meta.Rev,
            Extra: extra);
    }

    // ------------------------------------------------------------------
    // Path parsing.
    // ------------------------------------------------------------------

    /// <summary>
    /// Parses a dropbox:// URI into a rooted Dropbox path. Uses
    /// PathScheme.Parse so the optional [instance] segment is stripped
    /// correctly. Rejects a non-dropbox scheme or an empty path.
    /// Adds a leading slash when the URI carries the single-slash form
    /// (dropbox://folder/file.txt); the triple-slash and instance forms
    /// already yield a rooted path. The path is passed to the SDK
    /// unchanged - no URL decoding.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The path is not a dropbox:// path, or the path is empty.
    /// </exception>
    private static string ParseDropboxPath(string cloudPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cloudPath);

        var info = PathScheme.Parse(cloudPath);

        if (info.Kind != PathSchemeKind.Dropbox)
        {
            throw new ArgumentException(
                "Path does not start with dropbox://",
                nameof(cloudPath));
        }

        if (string.IsNullOrEmpty(info.Rest))
        {
            throw new ArgumentException(
                "dropbox:// path has no path.",
                nameof(cloudPath));
        }

        var path = info.Rest;
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return path;
    }
}
