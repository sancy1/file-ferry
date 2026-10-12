// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/IGDriveTransport.cs
// PURPOSE:  The narrow SDK seam the Drive adapter is built on. It isolates
//           the six concrete DriveService request calls behind an interface
//           so GDriveClientAdapter can be tested deterministically without
//           a live DriveService. The production implementation is
//           DriveServiceTransport. The methods return SDK types (FileList,
//           Data.File, IUploadProgress) because the adapter maps them; the
//           seam is at the transport boundary, not the DTO boundary.
// RELATION: Implemented by DriveServiceTransport. Consumed by
//           GDriveClientAdapter. Internal because it exposes SDK types.
// ============================================================================

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Drive.v3.Data;
using DriveFile = Google.Apis.Drive.v3.Data.File;
using Google.Apis.Upload;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// The six concrete Drive SDK operations the adapter needs.
/// </summary>
/// <remarks>
/// <para>
/// This seam exists because the Drive SDK's request types are generated
/// concrete classes and cannot be substituted. A stub implementation of
/// this interface lets the adapter be tested for query construction, page
/// token handling, upload metadata, not-found conversion, and cancellation
/// without a live DriveService.
/// </para>
/// <para>
/// <b>Cancellation contract.</b> Caller cancellation throws
/// <see cref="OperationCanceledException"/> unchanged.
/// </para>
/// </remarks>
internal interface IGDriveTransport
{
    /// <summary>
    /// Lists files matching a Drive query, with pagination.
    /// </summary>
    /// <param name="query">The Drive query string, constructed by the adapter.</param>
    /// <param name="pageSize">The page size, or null for the SDK default.</param>
    /// <param name="pageToken">The page token, or null for the first page.</param>
    /// <param name="ct">Cancels the list.</param>
    /// <returns>The SDK FileList response.</returns>
    Task<FileList> ListAsync(string query, int? pageSize, string? pageToken, CancellationToken ct);

    /// <summary>
    /// Fetches a file's metadata by id.
    /// </summary>
    /// <param name="fileId">The Drive file id.</param>
    /// <param name="ct">Cancels the fetch.</param>
    /// <returns>The SDK Data.File resource.</returns>
    Task<DriveFile> GetAsync(string fileId, CancellationToken ct);

    /// <summary>
    /// Deletes a file by id.
    /// </summary>
    /// <param name="fileId">The Drive file id.</param>
    /// <param name="ct">Cancels the delete.</param>
    Task DeleteAsync(string fileId, CancellationToken ct);

    /// <summary>
    /// Downloads a file's binary content into a stream.
    /// </summary>
    /// <param name="fileId">The Drive file id.</param>
    /// <param name="destination">The stream to write into.</param>
    /// <param name="ct">Cancels the download.</param>
    Task DownloadAsync(string fileId, Stream destination, CancellationToken ct);

    /// <summary>
    /// Exports a Google-native file to a MIME type into a stream.
    /// </summary>
    /// <param name="fileId">The Drive file id.</param>
    /// <param name="exportMimeType">The MIME type to export to.</param>
    /// <param name="destination">The stream to write into.</param>
    /// <param name="ct">Cancels the export.</param>
    Task ExportAsync(string fileId, string exportMimeType, Stream destination, CancellationToken ct);

    /// <summary>
    /// Updates the binary content of the existing file identified by
    /// <paramref name="fileId"/>. This targets that file id; it does not
    /// create a new file.
    /// </summary>
    /// <param name="fileId">The Drive file id of the file to update.</param>
    /// <param name="contentType">The content type of the new content.</param>
    /// <param name="content">The stream to upload as the new content.</param>
    /// <param name="ct">Cancels the update.</param>
    /// <returns>The SDK upload progress.</returns>
    Task<IUploadProgress> UpdateAsync(
        string fileId,
        string? contentType,
        Stream content,
        CancellationToken ct);

    /// <summary>
    /// Uploads content to a new file described by the given metadata.
    /// </summary>
    /// <param name="metadata">The file metadata (name, parents).</param>
    /// <param name="content">The stream to upload.</param>
    /// <param name="contentType">The content type of the stream.</param>
    /// <param name="ct">Cancels the upload.</param>
    /// <returns>The SDK upload progress.</returns>
    Task<IUploadProgress> UploadAsync(DriveFile metadata, Stream content, string contentType, CancellationToken ct);
}
