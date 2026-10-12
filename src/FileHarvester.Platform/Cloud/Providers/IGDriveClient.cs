// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/IGDriveClient.cs
// PURPOSE:  Thin wrapper interface over Google.Apis.Drive.v3.DriveService.
//           Exists because DriveService is a concrete generated class whose
//           request types are not mockable, so the provider cannot depend
//           on it directly and remain testable. GDriveClientAdapter
//           implements this over an IGDriveTransport seam. Tests substitute
//           this interface for the provider tests and the transport for
//           the adapter tests.
// RELATION: Implemented by GDriveClientAdapter. Consumed by
//           GoogleDriveProvider. Internal because it returns the internal
//           DTOs (GDriveObjectPage, GDriveObjectMetadata).
// ============================================================================

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// A thin wrapper over the Google Drive SDK surface that
/// <see cref="GoogleDriveProvider"/> needs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this interface exists.</b> The Drive SDK's
/// <c>DriveService</c> is a concrete generated class whose request types
/// cannot be substituted with NSubstitute. This interface is the mockable
/// boundary the provider depends on.
/// </para>
/// <para>
/// <b>Drive has no bucket or key.</b> Files are identified by file id.
/// Listing is per-folder, expressed by the adapter as a Drive query; the
/// provider never accepts raw query syntax from a caller.
/// </para>
/// <para>
/// <b>Cancellation contract.</b> When <paramref name="ct"/> is cancelled,
/// the method throws <see cref="OperationCanceledException"/> unchanged.
/// </para>
/// <para>
/// <b>Stream ownership.</b> A returned stream is owned by the caller. A
/// passed stream is consumed but not disposed by the implementation.
/// </para>
/// <para>
/// <b>Not-found contract.</b> <see cref="ExistsAsync"/> and
/// <see cref="DeleteIfExistsAsync"/> convert a not-found response to
/// <c>false</c> inside the adapter. Every other failure propagates.
/// </para>
/// </remarks>
internal interface IGDriveClient
{
    /// <summary>
    /// Downloads the binary content of a file. This is valid only for
    /// files stored in Drive with binary content. A Google-native document
    /// has no downloadable content and must be exported instead; the
    /// provider discovers that from metadata before calling this method.
    /// </summary>
    /// <param name="fileId">The Drive file id. Non-null, non-empty.</param>
    /// <param name="ct">Cancels the download.</param>
    /// <returns>A readable stream owned by the caller, positioned at 0.</returns>
    Task<Stream> DownloadAsync(string fileId, CancellationToken ct);

    /// <summary>
    /// Lists the children of a folder. The folder id is translated by the
    /// adapter into a constrained Drive query; a caller never supplies raw
    /// query syntax.
    /// </summary>
    /// <param name="folderId">The parent folder id. Non-null, non-empty.</param>
    /// <param name="continuation">
    /// The opaque page token from a previous call, or null for the first
    /// page.
    /// </param>
    /// <param name="max">
    /// The maximum number of items to return. Must be positive.
    /// </param>
    /// <param name="ct">Cancels the list.</param>
    /// <returns>One page of items and an optional continuation token.</returns>
    Task<GDriveObjectPage> ListAsync(
        string folderId,
        string? continuation,
        int max,
        CancellationToken ct);

    /// <summary>
    /// Uploads content to a new file, optionally under a parent folder.
    /// </summary>
    /// <param name="fileName">The name of the new file. Non-null, non-empty.</param>
    /// <param name="parentFolderId">
    /// The parent folder id, or null to place the file in the user's root.
    /// </param>
    /// <param name="contentType">
    /// The content type of the stream, or null to let the SDK choose.
    /// </param>
    /// <param name="content">
    /// The stream to upload. Consumed but not disposed by the implementation.
    /// </param>
    /// <param name="ct">Cancels the upload.</param>
    Task UploadAsync(
        string fileName,
        string? parentFolderId,
        string? contentType,
        Stream content,
        CancellationToken ct);

    /// <summary>
    /// Updates the binary content of the existing file identified by
    /// <paramref name="fileId"/>. The request targets that file id; it
    /// does not create a new file.
    /// </summary>
    /// <param name="fileId">The Drive file id of the file to update.</param>
    /// <param name="contentType">
    /// The content type of the new content, or null to let the SDK choose.
    /// </param>
    /// <param name="content">
    /// The stream to upload as the new content. Consumed but not disposed
    /// by the implementation.
    /// </param>
    /// <param name="ct">Cancels the update.</param>
    Task UpdateAsync(
        string fileId,
        string? contentType,
        Stream content,
        CancellationToken ct);

    /// <summary>
    /// Returns whether a file exists. A not-found response is converted
    /// to <c>false</c> inside the adapter; no exception is thrown for it.
    /// </summary>
    /// <param name="fileId">The Drive file id.</param>
    /// <param name="ct">Cancels the check.</param>
    /// <returns><c>true</c> when the file exists.</returns>
    Task<bool> ExistsAsync(string fileId, CancellationToken ct);

    /// <summary>
    /// Deletes a file if it exists. A not-found response is converted to
    /// <c>false</c> inside the adapter.
    /// </summary>
    /// <param name="fileId">The Drive file id.</param>
    /// <param name="ct">Cancels the delete.</param>
    /// <returns><c>true</c> when a file was deleted.</returns>
    Task<bool> DeleteIfExistsAsync(string fileId, CancellationToken ct);

    /// <summary>
    /// Retrieves metadata for a file.
    /// </summary>
    /// <param name="fileId">The Drive file id.</param>
    /// <param name="ct">Cancels the fetch.</param>
    /// <returns>The file metadata. Never null for an existing file.</returns>
    Task<GDriveObjectMetadata> GetPropertiesAsync(string fileId, CancellationToken ct);
}
