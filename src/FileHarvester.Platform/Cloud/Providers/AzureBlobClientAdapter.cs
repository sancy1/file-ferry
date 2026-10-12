// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/AzureBlobClientAdapter.cs
// PURPOSE:  Concrete implementation of IAzureBlobClient over a live
//           Azure.Storage.Blobs.BlobServiceClient. A thin translator: it
//           invokes the SDK, maps the SDK's responses to the wrapper's
//           DTOs, and returns. It never retries, never classifies
//           exceptions, never redacts, never logs. Those are the provider's
//           responsibilities (C.4.3).
// RELATION: Implements IAzureBlobClient. Constructed by the profile-to-
//           settings adapter in C.8. Consumed by AzureBlobProvider.
//           BlobServiceClient is injected by AzureBlobProviderFactory.
// ============================================================================

using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// Thin adapter from <see cref="BlobServiceClient"/> to
/// <see cref="IAzureBlobClient"/>.
/// </summary>
internal sealed class AzureBlobClientAdapter : IAzureBlobClient
{
    private readonly BlobServiceClient _client;

    // Container clients are lightweight and thread-safe to reuse. Cache
    // them by name. Blob clients are not cached: their names are effectively
    // unbounded, and constructing one is cheap.
    private readonly ConcurrentDictionary<string, BlobContainerClient> _containers
        = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates the adapter over the given service client.
    /// </summary>
    /// <param name="client">
    /// The live service client. Must not be null.
    /// </param>
    public AzureBlobClientAdapter(BlobServiceClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    // ------------------------------------------------------------------
    // IAzureBlobClient
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(
        string container,
        string blob,
        CancellationToken ct)
    {
        var blobClient = GetBlobClient(container, blob);

        var response = await blobClient
            .DownloadStreamingAsync(cancellationToken: ct)
            .ConfigureAwait(false);

        return response.Value.Content;
    }

    /// <inheritdoc />
    public async Task<AzureBlobPage> ListAsync(
        string container,
        string? prefix,
        string? continuation,
        int max,
        CancellationToken ct)
    {
        var containerClient = GetContainerClient(container);

        var names = new List<string>(capacity: Math.Min(max, 128));
        string? nextToken = null;

        var pages = containerClient
            .GetBlobsAsync(
                traits: default,
                states: default,
                prefix: prefix,
                cancellationToken: ct)
            .AsPages(continuationToken: continuation, pageSizeHint: max);

        await foreach (var page in pages.ConfigureAwait(false))
        {
            foreach (var item in page.Values)
            {
                if (names.Count >= max)
                {
                    break;
                }

                names.Add(item.Name);
            }

            if (names.Count >= max)
            {
                nextToken = page.ContinuationToken;
                break;
            }

            // Reached end of a page without hitting the cap. If the SDK
            // signals no more pages, nextToken stays null. Otherwise we
            // keep iterating.
            nextToken = page.ContinuationToken;
        }

        return new AzureBlobPage(names, nextToken);
    }

    /// <inheritdoc />
    public async Task UploadAsync(
        string container,
        string blob,
        Stream content,
        string? contentType,
        CancellationToken ct)
    {
        var blobClient = GetBlobClient(container, blob);

        // The overload with a BlobUploadOptions lets us set ContentType
        // when the caller provides one. When null, we use the simpler
        // overload and let the SDK default the type.
        if (string.IsNullOrWhiteSpace(contentType))
        {
            await blobClient
                .UploadAsync(content, overwrite: true, cancellationToken: ct)
                .ConfigureAwait(false);

            return;
        }

        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType,
            },
        };

        await blobClient
            .UploadAsync(content, options, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        string container,
        string blob,
        CancellationToken ct)
    {
        var blobClient = GetBlobClient(container, blob);

        var response = await blobClient
            .ExistsAsync(ct)
            .ConfigureAwait(false);

        return response.Value;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteIfExistsAsync(
        string container,
        string blob,
        CancellationToken ct)
    {
        var blobClient = GetBlobClient(container, blob);

        var response = await blobClient
            .DeleteIfExistsAsync(cancellationToken: ct)
            .ConfigureAwait(false);

        return response.Value;
    }

    /// <inheritdoc />
    public async Task<AzureBlobMetadata> GetPropertiesAsync(
        string container,
        string blob,
        CancellationToken ct)
    {
        var blobClient = GetBlobClient(container, blob);

        var response = await blobClient
            .GetPropertiesAsync(cancellationToken: ct)
            .ConfigureAwait(false);

        var props = response.Value;

        var extra = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["blob-type"]        = props.BlobType.ToString(),
            ["access-tier"]      = props.AccessTier ?? string.Empty,
            ["lease-state"]      = props.LeaseState.ToString(),
            ["server-encrypted"] = props.IsServerEncrypted.ToString(),
            ["tier-inferred"]    = props.AccessTierInferred.ToString(),
        };

        return new AzureBlobMetadata(
            SizeBytes:       props.ContentLength,
            LastModifiedUtc: props.LastModified.UtcDateTime,
            ContentType:     props.ContentType,
            ETag:            props.ETag.ToString(),
            Extra:           extra);
    }

    // ------------------------------------------------------------------
    // Client retrieval helpers.
    // ------------------------------------------------------------------

    private BlobContainerClient GetContainerClient(string container)
    {
        return _containers.GetOrAdd(
            container,
            static (name, client) => client.GetBlobContainerClient(name),
            _client);
    }

    private BlobClient GetBlobClient(string container, string blob)
    {
        return GetContainerClient(container).GetBlobClient(blob);
    }
}
