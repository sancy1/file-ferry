// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/OneDriveObjectMetadata.cs
// PURPOSE:  Internal DTO describing the metadata of a OneDrive item. Mirrors
//           GDriveObjectMetadata. ETag replaces Drive MD5 checksum; IsFolder
//           replaces Drive trashed. Marked internal because it is an
//           implementation detail of the OneDrive adapter.
// RELATION: Produced by IOneDriveClient.GetPropertiesAsync and consumed by
//           OneDriveProvider.GetMetadataAsync.
// ============================================================================

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>The metadata of a single OneDrive item.</summary>
/// <param name="Id">The OneDrive item id. Non-null.</param>
/// <param name="Name">The item name. Nullable.</param>
/// <param name="MimeType">The MIME type. Nullable. Null for a folder.</param>
/// <param name="SizeBytes">The size in bytes; zero when Graph reports none.</param>
/// <param name="LastModifiedUtc">The last-modified timestamp in UTC, or null.</param>
/// <param name="ETag">The entity tag, or null.</param>
/// <param name="IsFolder">True when the item is a folder.</param>
internal sealed record OneDriveObjectMetadata(
    string Id,
    string? Name,
    string? MimeType,
    long SizeBytes,
    DateTime? LastModifiedUtc,
    string? ETag,
    bool IsFolder);
