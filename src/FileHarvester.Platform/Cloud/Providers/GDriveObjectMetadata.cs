// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/GDriveObjectMetadata.cs
// PURPOSE:  Internal DTO describing the metadata of a Drive file. Mirrors
//           AzureBlobMetadata and GcsObjectMetadata in shape. Marked
//           internal because it is an implementation detail of the Drive
//           adapter and is not part of the public ICloudProvider contract.
// RELATION: Produced by IGDriveClient.GetPropertiesAsync and consumed by
//           GoogleDriveProvider.GetMetadataAsync, which translates it into
//           Core's ProviderMetadata record.
// ============================================================================

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// The metadata of a single Drive file.
/// </summary>
/// <param name="Id">The Drive file id. Non-null.</param>
/// <param name="Name">The file name. Nullable because Drive does not require one.</param>
/// <param name="MimeType">
/// The MIME type. Nullable. A value starting with
/// <c>application/vnd.google-apps.</c> identifies a Google-native document
/// that has no downloadable content and must be exported.
/// </param>
/// <param name="SizeBytes">
/// The size in bytes, or zero when Drive does not report a size (folders
/// and shortcuts have none). The Drive source field is nullable; this DTO
/// normalizes null to zero so the Core metadata contract (a non-nullable
/// long) is preserved.
/// </param>
/// <param name="LastModifiedUtc">
/// The last-modified timestamp in UTC, or null when Drive does not report
/// one. The mapping from the SDK's nullable DateTimeOffset preserves
/// null: a missing timestamp is not converted to a default date.
/// </param>
/// <param name="Md5Checksum">
/// The MD5 checksum, or null. Only present for binary content.
/// </param>
/// <param name="IsTrashed">
/// Whether the file is in the trash. Exposed so a listing can filter it.
/// </param>
internal sealed record GDriveObjectMetadata(
    string Id,
    string? Name,
    string? MimeType,
    long SizeBytes,
    DateTime? LastModifiedUtc,
    string? Md5Checksum,
    bool IsTrashed);
