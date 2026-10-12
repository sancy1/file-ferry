// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/GcsClientAdapter.cs
// PURPOSE:  Concrete implementation of IGcsClient over a live
//           Google.Cloud.Storage.V1.StorageClient. A thin translator: it
//           invokes the SDK, maps the SDK's responses to the wrapper's
//           DTOs, and returns. It never retries, never classifies, never
//           logs. Those are the provider's responsibilities (C.5.3).
// RELATION: Implements IGcsClient. Constructed by the profile-to-settings
//           adapter in C.8. Consumed by GcsObjectProvider. The
//           StorageClient is injected by GcsProviderFactory.
// ============================================================================

using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Google;
using Google.Cloud.Storage.V1;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// Thin adapter from <see cref="StorageClient"/> to
/// <see cref="IGcsClient"/>.
/// </summary>
internal sealed class GcsClientAdapter : IGcsClient
{
    private readonly StorageClient _client;

    /// <summary>
    /// Creates the adapter over the given service client.
    /// </summary>
    /// <param name="client">The live client. Must not be null.</param>
    public GcsClientAdapter(StorageClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    // ------------------------------------------------------------------
    // IGcsClient
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(
        string bucket,
        string objectName,
        CancellationToken ct)
    {
        // DownloadObjectAsync writes into the destination stream. We
        // supply a MemoryStream and return it. The SDK does not close
        // the destination, so the caller owns the returned stream.
        var destination = new MemoryStream();

        await _client.DownloadObjectAsync(
            bucket,
            objectName,
            destination,
            options: null,
            cancellationToken: ct).ConfigureAwait(false);

        destination.Position = 0;
        return destination;
    }

    /// <inheritdoc />
    public async Task<GcsObjectPage> ListAsync(
        string bucket,
        string prefix,
        string? continuation,
        int max,
        CancellationToken ct)
    {
        var options = new ListObjectsOptions
        {
            PageSize = max,
            PageToken = continuation,
        };

        // AsRawResponses() exposes the page token. We enumerate exactly
        // one raw page — never a second page. The NextPageToken is
        // preserved unchanged for the caller.
        var names = new List<string>(capacity: Math.Min(max, 128));
        string? nextToken = null;

        await foreach (var page in _client
            .ListObjectsAsync(bucket, prefix, options)
            .AsRawResponses()
            .WithCancellation(ct)
            .ConfigureAwait(false))
        {
            foreach (var obj in page.Items ?? Enumerable.Empty<Google.Apis.Storage.v1.Data.Object>())
            {
                if (obj.Name is not null)
                {
                    names.Add(obj.Name);
                }
            }

            nextToken = page.NextPageToken;
            break;
        }

        return new GcsObjectPage(names, nextToken);
    }

    /// <inheritdoc />
    public async Task UploadAsync(
        string bucket,
        string objectName,
        string? contentType,
        Stream content,
        CancellationToken ct)
    {
        // The content type is the third positional argument on
        // UploadObjectAsync. UploadObjectOptions has no ContentType
        // property; options is passed as null.
        await _client.UploadObjectAsync(
            bucket,
            objectName,
            contentType ?? "application/octet-stream",
            content,
            options: null,
            cancellationToken: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        string bucket,
        string objectName,
        CancellationToken ct)
    {
        // GetObjectAsync is the SDK's metadata call — its XML doc
        // states it "does not retrieve the content of the object
        // itself." A missing object throws GoogleApiException with
        // StatusCode = NotFound. The catch is inside the adapter,
        // BEFORE the resilience pipeline sees the call, so a genuine
        // 404 is never retried as a transient failure.
        try
        {
            await _client.GetObjectAsync(
                bucket,
                objectName,
                options: null,
                cancellationToken: ct).ConfigureAwait(false);
            return true;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteIfExistsAsync(
        string bucket,
        string objectName,
        CancellationToken ct)
    {
        // Same not-found contract as ExistsAsync: the catch is inside
        // the adapter, before the resilience policy. Every other
        // failure — 401, 403, 429, 5xx, network — propagates.
        try
        {
            await _client.DeleteObjectAsync(
                bucket,
                objectName,
                options: null,
                cancellationToken: ct).ConfigureAwait(false);
            return true;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<GcsObjectMetadata> GetPropertiesAsync(
        string bucket,
        string objectName,
        CancellationToken ct)
    {
        var obj = await _client.GetObjectAsync(
            bucket,
            objectName,
            options: null,
            cancellationToken: ct).ConfigureAwait(false);

        var extra = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["storage-class"]    = obj.StorageClass ?? string.Empty,
            ["generation"]       = obj.Generation?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["metageneration"]   = obj.Metageneration?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["md5-hash"]         = obj.Md5Hash ?? string.Empty,
            ["crc32c"]           = obj.Crc32c ?? string.Empty,
            ["component-count"]  = obj.ComponentCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        };

        return new GcsObjectMetadata(
            SizeBytes:       (long)(obj.Size ?? 0),
            LastModifiedUtc: obj.UpdatedDateTimeOffset?.UtcDateTime,
            ContentType:     obj.ContentType,
            ETag:            obj.ETag,
            Extra:           extra);
    }
}
