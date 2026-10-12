// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/DropboxClientAdapter.cs
// PURPOSE:  The Dropbox SDK implementation of IDropboxClient. Mirrors
//           MicrosoftGraphOneDriveClient: it validates inputs, maps Dropbox
//           FileMetadata/FolderMetadata to the internal DTOs, converts a
//           typed path/not_found to false only in the existence checks,
//           and lets every other failure propagate. Transient Dropbox
//           statuses are wrapped in IOException, which the existing
//           resilience policy already classifies as transient.
// RELATION: Implements IDropboxClient. Consumed by DropboxProvider.
// NOTES
//   Dropbox is path-oriented: every path parameter is a root-relative
//   Dropbox path beginning with a forward slash. The provider normalizes
//   the dropbox:// URL form to that path before calling; the adapter
//   passes the path unchanged. There is no URL decoding.
//   The not-found check is method-dependent. Existence checks tolerate
//   absence; reads, writes, metadata, and listing surface a missing path
//   as an error, matching MicrosoftGraphOneDriveClient and GDriveClientAdapter.
//   IDownloadResponse<T> is IDisposable and owns the HTTP response; the
//   adapter buffers its content into a MemoryStream and disposes the
//   response before returning, so the caller owns only the buffer.
//   The client does not retry. Resilience remains the policy responsibility.
//   OperationCanceledException is never caught here; it propagates.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dropbox.Api;
using Dropbox.Api.Files;
using Dropbox.Api.Stone;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>The Dropbox-backed client.</summary>
internal sealed class DropboxClientAdapter : IDropboxClient
{
    private const string TransientMessage =
        "Dropbox request failed with a transient status.";

    private readonly DropboxClient _client;

    /// <summary>Creates the adapter over a Dropbox client.</summary>
    /// <param name="client">The Dropbox client. Not null.</param>
    public DropboxClientAdapter(DropboxClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var buffer = new MemoryStream();
        try
        {
            using var response = await _client.Files
                .DownloadAsync(new DownloadArg(path))
                .ConfigureAwait(false);

            var stream = await response.GetContentAsStreamAsync()
                .ConfigureAwait(false);

            await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        }
        catch (ApiException<DownloadError> ex)
            when (ex.ErrorResponse.IsPath && ex.ErrorResponse.AsPath.Value.IsNotFound)
        {
            buffer.Dispose();
            throw;
        }
        catch (HttpException ex)
        {
            buffer.Dispose();
            throw Translate(ex);
        }

        buffer.Position = 0;
        return buffer;
    }

    /// <inheritdoc />
    public async Task<DropboxObjectPage> ListAsync(
        string path,
        string? continuation,
        int max,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (max <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(max), "max must be positive.");
        }

        ListFolderResult result;
        try
        {
            result = continuation is null
                ? await _client.Files.ListFolderAsync(new ListFolderArg(path, limit: (uint)max)).ConfigureAwait(false)
                : await _client.Files
                    .ListFolderContinueAsync(new ListFolderContinueArg(continuation))
                    .ConfigureAwait(false);
        }
        catch (HttpException ex)
        {
            throw Translate(ex);
        }

        var items = new List<DropboxObjectMetadata>(result.Entries.Count);
        foreach (var entry in result.Entries)
        {
            items.Add(Map(entry));
        }

        var next = result.HasMore ? result.Cursor : null;
        return new DropboxObjectPage(items, next);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(
        string path,
        string? contentType,
        Stream content,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        _ = contentType;

        try
        {
            await _client.Files
                .UploadAsync(new UploadArg(path, WriteMode.Overwrite.Instance), content)
                .ConfigureAwait(false);
        }
        catch (HttpException ex)
        {
            throw Translate(ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            await _client.Files
                .GetMetadataAsync(new GetMetadataArg(path))
                .ConfigureAwait(false);
            return true;
        }
        catch (ApiException<GetMetadataError> ex)
            when (ex.ErrorResponse.IsPath && ex.ErrorResponse.AsPath.Value.IsNotFound)
        {
            return false;
        }
        catch (HttpException ex)
        {
            throw Translate(ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteIfExistsAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            await _client.Files
                .DeleteV2Async(new DeleteArg(path))
                .ConfigureAwait(false);
            return true;
        }
        catch (ApiException<DeleteError> ex)
            when (ex.ErrorResponse.IsPathLookup && ex.ErrorResponse.AsPathLookup.Value.IsNotFound)
        {
            return false;
        }
        catch (HttpException ex)
        {
            throw Translate(ex);
        }
    }

    /// <inheritdoc />
    public async Task<DropboxObjectMetadata> GetPropertiesAsync(
        string path,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Metadata? meta;
        try
        {
            meta = await _client.Files
                .GetMetadataAsync(new GetMetadataArg(path))
                .ConfigureAwait(false);
        }
        catch (HttpException ex)
        {
            throw Translate(ex);
        }

        return Map(meta);
    }

    // ------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------

    /// <summary>
    /// Translates a transient Dropbox status into an IOException, which
    /// the existing resilience policy already classifies as transient.
    /// Every other ApiException is returned unchanged and is treated as
    /// permanent.
    /// </summary>
    private static Exception Translate(HttpException ex)
    {
        if (ex.StatusCode is 408 or 429 or 500 or 502 or 503 or 504)
        {
            return new IOException(TransientMessage, ex);
        }

        return ex;
    }

    /// <summary>Maps a Dropbox Metadata to the internal DTO.</summary>
    private static DropboxObjectMetadata Map(Metadata? entry)
    {
        return entry switch
        {
            FileMetadata file => new DropboxObjectMetadata(
                Id: file.Id,
                PathLower: file.PathLower,
                Name: file.Name,
                Rev: file.Rev,
                SizeBytes: (long)file.Size,
                ServerModifiedUtc: file.ServerModified.ToUniversalTime(),
                ContentHash: file.ContentHash,
                IsFolder: false),
            FolderMetadata folder => new DropboxObjectMetadata(
                Id: folder.Id,
                PathLower: folder.PathLower,
                Name: folder.Name,
                Rev: null,
                SizeBytes: 0,
                ServerModifiedUtc: null,
                ContentHash: null,
                IsFolder: true),
            _ => throw new InvalidOperationException(
                "Dropbox returned metadata that is neither a file nor a folder."),
        };
    }
}
