// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/DriveServiceTransport.cs
// PURPOSE:  The production IGDriveTransport over a live DriveService. Each
//           method makes exactly one Drive SDK request. No retry, no
//           classification, no logging, no resilience: those live in the
//           provider. The transport is a thin, deterministic translation
//           from the seam to the SDK.
// RELATION: Implements IGDriveTransport. Constructed by the C.8 wiring with
//           an authorized DriveService. Consumed by GDriveClientAdapter.
// ============================================================================

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Upload;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// The live Drive SDK transport for the adapter.
/// </summary>
internal sealed class DriveServiceTransport : IGDriveTransport
{
    private readonly DriveService _service;

    /// <summary>
    /// Creates the transport over an authorized Drive service.
    /// </summary>
    /// <param name="service">The Drive service. Must not be null.</param>
    public DriveServiceTransport(DriveService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    /// <inheritdoc />
    public async Task<FileList> ListAsync(
        string query,
        int? pageSize,
        string? pageToken,
        CancellationToken ct)
    {
        var request = _service.Files.List();
        request.Q = query;
        request.PageSize = pageSize;
        request.PageToken = pageToken;

        return await request.ExecuteAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DriveFile> GetAsync(string fileId, CancellationToken ct)
    {
        return await _service.Files.Get(fileId).ExecuteAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string fileId, CancellationToken ct)
    {
        await _service.Files.Delete(fileId).ExecuteAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DownloadAsync(string fileId, Stream destination, CancellationToken ct)
    {
        await _service.Files.Get(fileId).DownloadAsync(destination, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ExportAsync(
        string fileId,
        string exportMimeType,
        Stream destination,
        CancellationToken ct)
    {
        await _service.Files.Export(fileId, exportMimeType).DownloadAsync(destination, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IUploadProgress> UpdateAsync(
        string fileId,
        string? contentType,
        Stream content,
        CancellationToken ct)
    {
        var update = _service.Files.Update(
            new DriveFile(),
            fileId,
            content,
            contentType ?? "application/octet-stream");

        return await update.UploadAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IUploadProgress> UploadAsync(
        DriveFile metadata,
        Stream content,
        string contentType,
        CancellationToken ct)
    {
        var upload = _service.Files.Create(metadata, content, contentType);
        return await upload.UploadAsync(ct).ConfigureAwait(false);
    }
}
