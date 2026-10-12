// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/DropboxObjectPage.cs
// PURPOSE:  Internal DTO for one page of a Dropbox folder listing. The
//           continuation is the opaque Dropbox list cursor, preserved
//           unchanged and passed back to ListFolderContinueAsync.
// RELATION: Produced by IDropboxClient.ListAsync and consumed by
//           DropboxProvider.ListAsync.
// ============================================================================

using System.Collections.Generic;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>One page of a Dropbox folder listing.</summary>
/// <param name="Items">The entries in this page. Never null. May be empty.</param>
/// <param name="ContinuationToken">
/// The opaque Dropbox list cursor for the next page, or null when this is
/// the last page. It is preserved unchanged; the caller must not parse it.
/// </param>
internal sealed record DropboxObjectPage(
    IReadOnlyList<DropboxObjectMetadata> Items,
    string? ContinuationToken);
