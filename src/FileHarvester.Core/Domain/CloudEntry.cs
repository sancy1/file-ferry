// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/CloudEntry.cs
// PURPOSE:  One entry in a cloud listing: an object or a directory-like
//           prefix. URI-shaped path plus the metadata a listing surfaces.
// RELATION: Produced by ICloudProvider.ListAsync. Consumed by the cloud
//           file picker and by CloudPathSearchService.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// One entry in a cloud listing.
/// </summary>
/// <param name="Path">The URI-shaped path. Never null or empty.</param>
/// <param name="Name">The last path segment. Never null.</param>
/// <param name="IsDirectory">True when this entry is a directory-like prefix.</param>
/// <param name="SizeBytes">Size in bytes. Zero for directories.</param>
/// <param name="LastModifiedUtc">Last modified timestamp in UTC, or null.</param>
/// <param name="ContentType">MIME type when exposed by the provider, or null.</param>
public sealed record CloudEntry(
    string Path,
    string Name,
    bool IsDirectory,
    long SizeBytes,
    DateTime? LastModifiedUtc,
    string? ContentType);
