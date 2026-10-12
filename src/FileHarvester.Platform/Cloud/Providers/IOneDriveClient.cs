// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/IOneDriveClient.cs
// PURPOSE:  The mockable seam over Microsoft Graph that OneDriveProvider
//           needs. Mirrors IGDriveClient: six methods, DTO-level returns,
//           the same not-found contract. Internal because it returns the
//           internal DTOs.
// RELATION: Implemented by MicrosoftGraphOneDriveClient. Consumed by
//           OneDriveProvider.
// NOTES
//   OneDrive has no bucket or key. Items are identified by item id.
//   The continuation value is opaque; the caller never parses it.
// ============================================================================

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>A thin wrapper over the Microsoft Graph surface OneDrive needs.</summary>
internal interface IOneDriveClient
{
    /// <summary>Downloads the binary content of an item.</summary>
    /// <param name="itemId">The OneDrive item id. Non-null, non-empty.</param>
    /// <param name="ct">Cancels the download.</param>
    /// <returns>A readable stream owned by the caller, positioned at 0.</returns>
    Task<Stream> DownloadAsync(string itemId, CancellationToken ct);

    /// <summary>Lists the children of a folder.</summary>
    /// <param name="folderId">The parent folder item id.</param>
    /// <param name="continuation">The opaque continuation value, or null.</param>
    /// <param name="max">The maximum number of items to return.</param>
    /// <param name="ct">Cancels the list.</param>
    /// <returns>One page of items and an optional continuation value.</returns>
    Task<OneDriveObjectPage> ListAsync(
        string folderId,
        string? continuation,
        int max,
        CancellationToken ct);

    /// <summary>Updates the content of the existing item identified by itemId.</summary>
    /// <param name="itemId">The OneDrive item id of the item to update.</param>
    /// <param name="contentType">The content type, or null.</param>
    /// <param name="content">The stream to upload as the new content.</param>
    /// <param name="ct">Cancels the update.</param>
    Task UpdateAsync(
        string itemId,
        string? contentType,
        Stream content,
        CancellationToken ct);

    /// <summary>Returns whether an item exists. Not-found is false.</summary>
    Task<bool> ExistsAsync(string itemId, CancellationToken ct);

    /// <summary>Deletes an item if it exists. Not-found is false.</summary>
    Task<bool> DeleteIfExistsAsync(string itemId, CancellationToken ct);

    /// <summary>Retrieves metadata for an item.</summary>
    Task<OneDriveObjectMetadata> GetPropertiesAsync(string itemId, CancellationToken ct);
}
