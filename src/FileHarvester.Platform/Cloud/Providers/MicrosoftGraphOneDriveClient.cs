// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/MicrosoftGraphOneDriveClient.cs
// PURPOSE:  The Microsoft Graph implementation of IOneDriveClient. Mirrors
//           GDriveClientAdapter: it validates inputs, maps Graph DriveItem
//           responses to the internal DTOs, converts a 404 to false only in
//           the existence checks, and lets every other failure propagate.
//           Transient Graph statuses are wrapped in IOException, which the
//           existing resilience policy already classifies as transient.
// RELATION: Implements IOneDriveClient. Consumed by OneDriveProvider.
// NOTES
//   The 404-to-false conversion is method-dependent. Existence checks
//   tolerate absence; reads, writes, metadata, and listing surface a
//   missing item as an error, matching GDriveClientAdapter.
//   The client does not retry. Resilience remains the policy responsibility.
//   OperationCanceledException is never caught here; it propagates.
//   ListAsync follows the Graph @odata.nextLink continuation: the opaque
//   URL returned by the server is passed unchanged to Children.WithUrl,
//   and the page's OdataNextLink is returned as the next continuation.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>The Graph-backed OneDrive client.</summary>
internal sealed class MicrosoftGraphOneDriveClient : IOneDriveClient
{
    private const string TransientMessage =
        "Microsoft Graph request failed with a transient status.";

    private readonly GraphServiceClient _graph;
    private readonly string _driveId;

    /// <summary>Creates the client over a Graph client and a drive id.</summary>
    /// <param name="graph">The Graph client. Not null.</param>
    /// <param name="driveId">The drive id. Not null.</param>
    public MicrosoftGraphOneDriveClient(GraphServiceClient graph, string driveId)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _driveId = driveId ?? throw new ArgumentNullException(nameof(driveId));
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(string itemId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        var buffer = new MemoryStream();
        try
        {
            var stream = await _graph.Drives[_driveId].Items[itemId].Content
                .GetAsync(cancellationToken: ct).ConfigureAwait(false);
            if (stream is null)
            {
                throw new InvalidOperationException("Graph returned no content for the item.");
            }
            await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        }
        catch (ApiException ex)
        {
            buffer.Dispose();
            throw Translate(ex);
        }

        buffer.Position = 0;
        return buffer;
    }

    /// <inheritdoc />
    public async Task<OneDriveObjectPage> ListAsync(
        string folderId,
        string? continuation,
        int max,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderId);
        if (max <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(max), "max must be positive.");
        }

        DriveItemCollectionResponse? page;
        try
        {
            var builder = _graph.Drives[_driveId].Items[folderId].Children;

            if (string.IsNullOrEmpty(continuation))
            {
                page = await builder
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Top = max;
                    }, ct).ConfigureAwait(false);
            }
            else
            {
                // F-132: the continuation is the opaque @odata.nextLink URL
                // the server returned. It is passed unchanged to the SDK's
                // WithUrl; the URL is never parsed or rebuilt. Top is not
                // re-specified: the next link already encodes the server's
                // paging state.
                page = await builder
                    .WithUrl(continuation)
                    .GetAsync(requestConfiguration => { }, ct)
                    .ConfigureAwait(false);
            }
        }
        catch (ApiException ex)
        {
            throw Translate(ex);
        }

        var items = new List<OneDriveObjectMetadata>();
        if (page?.Value is not null)
        {
            foreach (var item in page.Value)
            {
                items.Add(Map(item));
            }
        }

        return new OneDriveObjectPage(items, page?.OdataNextLink);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(
        string itemId,
        string? contentType,
        Stream content,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(content);

        try
        {
            await _graph.Drives[_driveId].Items[itemId].Content
                .PutAsync(content, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (ApiException ex)
        {
            throw Translate(ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string itemId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        try
        {
            await _graph.Drives[_driveId].Items[itemId]
                .GetAsync(cancellationToken: ct).ConfigureAwait(false);
            return true;
        }
        catch (ApiException ex) when (ex.ResponseStatusCode == 404)
        {
            return false;
        }
        catch (ApiException ex)
        {
            throw Translate(ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteIfExistsAsync(string itemId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        try
        {
            await _graph.Drives[_driveId].Items[itemId]
                .DeleteAsync(cancellationToken: ct).ConfigureAwait(false);
            return true;
        }
        catch (ApiException ex) when (ex.ResponseStatusCode == 404)
        {
            return false;
        }
        catch (ApiException ex)
        {
            throw Translate(ex);
        }
    }

    /// <inheritdoc />
    public async Task<OneDriveObjectMetadata> GetPropertiesAsync(
        string itemId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        DriveItem? item;
        try
        {
            item = await _graph.Drives[_driveId].Items[itemId]
                .GetAsync(cancellationToken: ct).ConfigureAwait(false);
        }
        catch (ApiException ex)
        {
            throw Translate(ex);
        }

        return Map(item);
    }

    // ------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------

    /// <summary>
    /// Translates a transient Graph status into an IOException, which the
    /// existing resilience policy already classifies as transient. Every
    /// other ApiException is returned unchanged and is treated as permanent.
    /// </summary>
    private static Exception Translate(ApiException ex)
    {
        if (ex.ResponseStatusCode is 408 or 429 or 500 or 502 or 503 or 504)
        {
            return new IOException(TransientMessage, ex);
        }

        return ex;
    }

    /// <summary>Maps a Graph DriveItem to the internal DTO.</summary>
    private static OneDriveObjectMetadata Map(DriveItem? item)
    {
        if (item is null || string.IsNullOrEmpty(item.Id))
        {
            throw new InvalidOperationException("Graph returned an item with no id.");
        }

        return new OneDriveObjectMetadata(
            Id: item.Id,
            Name: item.Name,
            MimeType: item.File?.MimeType,
            SizeBytes: item.Size ?? 0,
            LastModifiedUtc: item.LastModifiedDateTime?.UtcDateTime,
            ETag: item.ETag,
            IsFolder: item.Folder is not null);
    }
}
