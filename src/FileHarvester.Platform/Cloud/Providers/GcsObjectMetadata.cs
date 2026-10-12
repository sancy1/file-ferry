// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/GcsObjectMetadata.cs
// PURPOSE:  Internal DTO describing the metadata of a GCS object. Carries
//           size, last-modified, content type, ETag, and a small map of
//           extra fields the provider translates into Core's ProviderMetadata.
//           Marked internal because it is an implementation detail of the
//           GCS adapter and is not part of the public ICloudProvider
//           contract. The test assembly reaches it via InternalsVisibleTo.
// RELATION: Produced by IGcsClient.GetPropertiesAsync and consumed by
//           GcsObjectProvider.GetMetadataAsync, which translates it into
//           FileHarvester.Core's ProviderMetadata record. Never carries
//           a credential value.
// ============================================================================

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// The metadata of a single GCS object.
/// </summary>
/// <param name="SizeBytes">
/// The object's size in bytes. Uses <see cref="long"/> because GCS
/// objects can be multiple terabytes.
/// </param>
/// <param name="LastModifiedUtc">
/// The object's last-modified timestamp, in UTC. Nullable because some
/// GCS responses do not include it.
/// </param>
/// <param name="ContentType">
/// The object's declared content type (for example <c>text/markdown</c>).
/// Nullable because GCS does not require it.
/// </param>
/// <param name="ETag">
/// The object's entity tag, suitable for optimistic concurrency checks.
/// Nullable because GCS does not always return one.
/// </param>
/// <param name="Extra">
/// A small read-only map of additional GCS-specific fields the provider
/// surfaces in the pipeline's metadata dictionary. Common keys:
/// <list type="bullet">
///   <item><c>storage-class</c> — STANDARD, NEARLINE, COLDLINE, ARCHIVE.</item>
///   <item><c>generation</c> — the object generation number.</item>
///   <item><c>metageneration</c> — the metadata generation number.</item>
///   <item><c>md5-hash</c> — the base64 MD5 hash, when present.</item>
///   <item><c>crc32c</c> — the base64 CRC32C hash, when present.</item>
///   <item><c>component-count</c> — the number of components for a composite object.</item>
/// </list>
/// Never null. May be empty. Never carries a credential value.
/// </param>
internal sealed record GcsObjectMetadata(
    long SizeBytes,
    DateTime? LastModifiedUtc,
    string? ContentType,
    string? ETag,
    IReadOnlyDictionary<string, string> Extra);
