// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/IDropboxClient.cs
// PURPOSE:  The mockable seam over the Dropbox SDK that DropboxProvider
//           needs. Mirrors IGDriveClient and IOneDriveClient: six methods,
//           DTO-level returns, the same not-found contract. Internal
//           because it returns the internal DTOs.
// RELATION: Implemented by DropboxClientAdapter. Consumed by DropboxProvider.
// NOTES
//   Dropbox is path-oriented: every path parameter is a root-relative
//   Dropbox path beginning with a forward slash. The provider normalizes
//   the dropbox:// URL form to that path before calling.
//   The continuation is the opaque Dropbox list cursor.
// ============================================================================

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>A thin wrapper over the Dropbox SDK surface Dropbox needs.</summary>
internal interface IDropboxClient
{
    /// <summary>Downloads the content of a file by path.</summary>
    /// <param name="path">The root-relative Dropbox path. Non-null.</param>
    /// <param name="ct">Cancels the download.</param>
    /// <returns>A readable stream owned by the caller, positioned at 0.</returns>
    Task<Stream> DownloadAsync(string path, CancellationToken ct);

    /// <summary>Lists the entries of a folder by path.</summary>
    /// <param name="path">The root-relative Dropbox folder path.</param>
    /// <param name="continuation">The opaque list cursor, or null.</param>
    /// <param name="max">The maximum number of entries to request.</param>
    /// <param name="ct">Cancels the list.</param>
    /// <returns>One page of entries and an optional continuation.</returns>
    Task<DropboxObjectPage> ListAsync(
        string path,
        string? continuation,
        int max,
        CancellationToken ct);

    /// <summary>
    /// Writes content to the given path, overwriting any existing file.
    /// </summary>
    /// <param name="path">The root-relative Dropbox path.</param>
    /// <param name="contentType">The content type, or null.</param>
    /// <param name="content">The stream to upload.</param>
    /// <param name="ct">Cancels the upload.</param>
    Task UpdateAsync(
        string path,
        string? contentType,
        Stream content,
        CancellationToken ct);

    /// <summary>Returns whether a path exists. A path/not_found is false.</summary>
    Task<bool> ExistsAsync(string path, CancellationToken ct);

    /// <summary>Deletes a path if it exists. A path/not_found is false.</summary>
    Task<bool> DeleteIfExistsAsync(string path, CancellationToken ct);

    /// <summary>Retrieves metadata for a path.</summary>
    Task<DropboxObjectMetadata> GetPropertiesAsync(string path, CancellationToken ct);
}
