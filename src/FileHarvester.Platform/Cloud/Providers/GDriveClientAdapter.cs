// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/GDriveClientAdapter.cs
// PURPOSE:  The adapter over IGDriveTransport. Implements both IGDriveClient
//           and IGDriveExportClient. It builds the constrained folder query,
//           maps SDK types to the internal DTOs, converts not-found
//           responses to false for existence and deletion, and surfaces a
//           failed upload as an exception. It holds no state between calls.
// RELATION: Implements IGDriveClient and IGDriveExportClient. Consumed by
//           GoogleDriveProvider. Depends on IGDriveTransport, which is
//           substituted in tests.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Google;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Upload;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// The Drive adapter over <see cref="IGDriveTransport"/>.
/// </summary>
/// <remarks>
/// The adapter validates its inputs before any transport call, preserves
/// cancellation and every non-not-found exception unchanged, and disposes
/// a buffer it created when the transport call throws before ownership is
/// transferred to the caller.
/// </remarks>
internal sealed class GDriveClientAdapter : IGDriveClient, IGDriveExportClient
{
    private readonly IGDriveTransport _transport;

    /// <summary>
    /// Creates the adapter over the transport. The transport must not be null.
    /// </summary>
    /// <param name="transport">The Drive transport.</param>
    public GDriveClientAdapter(IGDriveTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(string fileId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);

        var buffer = new MemoryStream();
        try
        {
            await _transport.DownloadAsync(fileId, buffer, ct).ConfigureAwait(false);
        }
        catch
        {
            buffer.Dispose();
            throw;
        }

        buffer.Position = 0;
        return buffer;
    }

    /// <inheritdoc />
    public async Task<GDriveObjectPage> ListAsync(
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

        // The constrained query, per the design: list the children of the
        // folder and exclude trashed files. The caller never supplies raw
        // Drive query syntax.
        var query = "'" + folderId + "' in parents and trashed = false";

        var list = await _transport.ListAsync(query, max, continuation, ct).ConfigureAwait(false);

        var items = new List<GDriveObjectMetadata>();
        if (list.Files is not null)
        {
            foreach (var f in list.Files)
            {
                items.Add(Map(f));
            }
        }

        return new GDriveObjectPage(items, list.NextPageToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(
        string fileId,
        string? contentType,
        Stream content,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentNullException.ThrowIfNull(content);

        var progress = await _transport
            .UpdateAsync(fileId, contentType ?? "application/octet-stream", content, ct)
            .ConfigureAwait(false);

        progress.ThrowOnFailure();
    }

    /// <inheritdoc />
    public async Task UploadAsync(
        string fileName,
        string? parentFolderId,
        string? contentType,
        Stream content,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);

        var file = new DriveFile { Name = fileName };
        if (!string.IsNullOrWhiteSpace(parentFolderId))
        {
            file.Parents = new List<string> { parentFolderId! };
        }

        var progress = await _transport
            .UploadAsync(file, content, contentType ?? "application/octet-stream", ct)
            .ConfigureAwait(false);

        // A failed upload returns a progress whose Status is Failed rather
        // than throwing. Surface it as an exception so the caller sees a
        // failure, and so no caller can mistake a failed upload for success.
        progress.ThrowOnFailure();
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string fileId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);

        try
        {
            await _transport.GetAsync(fileId, ct).ConfigureAwait(false);
            return true;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteIfExistsAsync(string fileId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);

        try
        {
            await _transport.DeleteAsync(fileId, ct).ConfigureAwait(false);
            return true;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<GDriveObjectMetadata> GetPropertiesAsync(string fileId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);

        var file = await _transport.GetAsync(fileId, ct).ConfigureAwait(false);
        return Map(file);
    }

    /// <inheritdoc />
    public async Task<Stream> ExportAsync(
        string fileId,
        string exportMimeType,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(exportMimeType);

        var buffer = new MemoryStream();
        try
        {
            await _transport.ExportAsync(fileId, exportMimeType, buffer, ct).ConfigureAwait(false);
        }
        catch
        {
            buffer.Dispose();
            throw;
        }

        buffer.Position = 0;
        return buffer;
    }

    // ------------------------------------------------------------------
    // Mapping.
    // ------------------------------------------------------------------

    /// <summary>
    /// Maps an SDK Drive file to the internal DTO. A Drive file always has
    /// an id; a response without one is malformed and throws.
    /// </summary>
    private static GDriveObjectMetadata Map(DriveFile file)
    {
        if (string.IsNullOrEmpty(file.Id))
        {
            throw new InvalidOperationException("Drive returned a file with no id.");
        }

        return new GDriveObjectMetadata(
            Id: file.Id,
            Name: file.Name,
            MimeType: file.MimeType,
            SizeBytes: file.Size ?? 0,
            LastModifiedUtc: file.ModifiedTimeDateTimeOffset?.UtcDateTime,
            Md5Checksum: file.Md5Checksum,
            IsTrashed: file.Trashed ?? false);
    }
}
