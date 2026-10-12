// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/OneDriveObjectPage.cs
// PURPOSE:  Internal DTO for one page of a OneDrive folder listing. Carries
//           the items and the opaque continuation value, which is preserved
//           unchanged. The value may be a full Graph next-link URL, but it
//           is treated as an opaque token here and never parsed.
// RELATION: Produced by IOneDriveClient.ListAsync and consumed by
//           OneDriveProvider.ListAsync.
// ============================================================================

using System.Collections.Generic;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>One page of a OneDrive folder listing.</summary>
/// <param name="Items">The items in this page. Never null. May be empty.</param>
/// <param name="ContinuationToken">
/// The opaque continuation value for the next page, or null when this is
/// the last page. It is preserved unchanged; the caller must not parse it.
/// </param>
internal sealed record OneDriveObjectPage(
    IReadOnlyList<OneDriveObjectMetadata> Items,
    string? ContinuationToken);
