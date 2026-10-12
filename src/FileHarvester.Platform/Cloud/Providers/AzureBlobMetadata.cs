// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/AzureBlobMetadata.cs
// PURPOSE:  Internal DTO describing the metadata of an Azure Blob. Carries
//           size, last-modified, content type, ETag, and a small map of
//           extra fields the provider translates into Core's ProviderMetadata.
//           Marked internal because it is an implementation detail of the
//           Azure adapter and is not part of the public ICloudProvider
//           contract. The test assembly reaches it via InternalsVisibleTo.
// RELATION: Produced by IAzureBlobClient.GetPropertiesAsync and consumed by
//           AzureBlobProvider.GetMetadataAsync (C.4.3), which translates it
//           into FileHarvester.Core's ProviderMetadata record. Never
//           carries a credential value.
// ============================================================================

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// The metadata of a single Azure Blob.
/// </summary>
/// <param name="SizeBytes">
/// The blob's content length in bytes. Uses <see cref="long"/> because
/// Azure Blob supports blobs up to approximately 190 TB.
/// </param>
/// <param name="LastModifiedUtc">
/// The blob's last-modified timestamp, in UTC. Nullable because some Azure
/// responses do not include it.
/// </param>
/// <param name="ContentType">
/// The blob's declared content type (for example <c>text/markdown</c>).
/// Nullable because Azure does not require it.
/// </param>
/// <param name="ETag">
/// The blob's entity tag, suitable for optimistic concurrency checks.
/// Nullable because Azure does not always return one.
/// </param>
/// <param name="Extra">
/// A small read-only map of additional Azure-specific fields the provider
/// surfaces in the pipeline's metadata dictionary. Common keys:
/// <list type="bullet">
///   <item><c>blob-type</c> — BlockBlob, PageBlob, or AppendBlob.</item>
///   <item><c>access-tier</c> — Hot, Cool, Cold, Archive, or null.</item>
///   <item><c>lease-state</c> — Available, Leased, Expired, or Breaking.</item>
///   <item><c>server-encrypted</c> — <c>true</c> or <c>false</c>.</item>
///   <item><c>tier-inferred</c> — <c>true</c> or <c>false</c>, or null.</item>
/// </list>
/// Never null. May be empty. Never carries a credential value.
/// </param>
internal sealed record AzureBlobMetadata(
    long SizeBytes,
    DateTime? LastModifiedUtc,
    string? ContentType,
    string? ETag,
    IReadOnlyDictionary<string, string> Extra);
