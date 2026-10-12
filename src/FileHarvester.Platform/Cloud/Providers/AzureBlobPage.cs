// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/AzureBlobPage.cs
// PURPOSE:  Internal DTO representing one page of a container listing. A
//           page contains up to N blob names and an opaque continuation
//           token that, when non-null, is passed back to the next ListAsync
//           call to retrieve the following page.
//           Marked internal because it is an implementation detail of the
//           Azure adapter and is not part of the public ICloudProvider
//           contract. The test assembly reaches it via InternalsVisibleTo.
// RELATION: Produced by IAzureBlobClient.ListAsync and consumed by
//           AzureBlobProvider.ListAsync (C.4.3), which translates it into
//           FileHarvester.Core's CloudEntryPage record. Never carries a
//           credential value.
// ============================================================================

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// One page of results from an Azure container listing.
/// </summary>
/// <param name="BlobNames">
/// The blob names contained in this page. Names are relative to the
/// container — they do not include the account, endpoint, or container
/// prefix. A name may contain slashes for nested paths; Azure treats
/// slashes as part of the name, not as folder separators. Never null.
/// May be empty when the container has no matching blobs.
/// </param>
/// <param name="ContinuationToken">
/// Opaque token to pass to the next <c>ListAsync</c> call to retrieve
/// the following page. Null when this is the last page. Non-null
/// otherwise. The token's contents are defined by the Azure SDK and
/// must not be parsed, modified, or relied upon for any property other
/// than its null/non-null state.
/// </param>
internal sealed record AzureBlobPage(
    IReadOnlyList<string> BlobNames,
    string? ContinuationToken);
