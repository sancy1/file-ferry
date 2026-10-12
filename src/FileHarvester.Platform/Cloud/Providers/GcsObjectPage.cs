// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/GcsObjectPage.cs
// PURPOSE:  Internal DTO representing one page of a bucket listing. A
//           page contains up to N object names and an opaque continuation
//           token that, when non-null, is passed back to the next
//           ListAsync call to retrieve the following page. The token is
//           the GCS NextPageToken, preserved unchanged.
//           Marked internal because it is an implementation detail of the
//           GCS adapter and is not part of the public ICloudProvider
//           contract. The test assembly reaches it via InternalsVisibleTo.
// RELATION: Produced by IGcsClient.ListAsync and consumed by
//           GcsObjectProvider.ListAsync, which translates it into
//           FileHarvester.Core's CloudEntryPage record. Never carries
//           a credential value.
// ============================================================================

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// One page of results from a GCS bucket listing.
/// </summary>
/// <param name="ObjectNames">
/// The object names contained in this page. Names are relative to the
/// bucket — they do not include the bucket or any scheme prefix. A name
/// may contain slashes for nested paths; GCS treats slashes as part of
/// the name, not as folder separators. Never null. May be empty when the
/// bucket or prefix has no matching objects.
/// </param>
/// <param name="ContinuationToken">
/// Opaque token to pass to the next <c>ListAsync</c> call to retrieve
/// the following page. Null when this is the last page. Non-null
/// otherwise. The token is the GCS NextPageToken and must not be parsed,
/// modified, or relied upon for any property other than its null/non-null
/// state.
/// </param>
internal sealed record GcsObjectPage(
    IReadOnlyList<string> ObjectNames,
    string? ContinuationToken);
