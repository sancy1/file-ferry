// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/GDriveObjectPage.cs
// PURPOSE:  Internal DTO for one page of a Drive folder listing. Carries
//           the items and the opaque next-page token, which is preserved
//           unchanged. Marked internal, mirroring AzureBlobPage and
//           GcsObjectPage.
// RELATION: Produced by IGDriveClient.ListAsync and consumed by
//           GoogleDriveProvider.ListAsync, which translates it into Core's
//           CloudEntryPage record.
// ============================================================================

using System.Collections.Generic;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// One page of a Drive folder listing.
/// </summary>
/// <param name="Items">
/// The metadata of the files in this page. Never null. May be empty.
/// </param>
/// <param name="ContinuationToken">
/// The opaque token for the next page, or null when this is the last
/// page. It is the Drive nextPageToken, preserved unchanged. It must not
/// be parsed, and the caller must not rely on any property of it other
/// than whether it is null.
/// </param>
internal sealed record GDriveObjectPage(
    IReadOnlyList<GDriveObjectMetadata> Items,
    string? ContinuationToken);
