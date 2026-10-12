// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/DropboxObjectMetadata.cs
// PURPOSE:  Internal DTO describing the metadata of a Dropbox file or
//           folder. Mirrors GDriveObjectMetadata and OneDriveObjectMetadata.
//           Dropbox is path-oriented, so PathLower and Name are carried
//           alongside the id; Rev is the Dropbox revision string.
// RELATION: Produced by IDropboxClient.GetPropertiesAsync and consumed by
//           DropboxProvider.GetMetadataAsync.
// ============================================================================

using System;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>The metadata of a single Dropbox entry.</summary>
/// <param name="Id">The Dropbox entry id.</param>
/// <param name="PathLower">The lowercased root-relative path, for identity.</param>
/// <param name="Name">The display name.</param>
/// <param name="Rev">The revision string, or null for a folder.</param>
/// <param name="SizeBytes">The size in bytes; zero for a folder.</param>
/// <param name="ServerModifiedUtc">The server-modified timestamp in UTC, or null.</param>
/// <param name="ContentHash">The Dropbox content hash, or null for a folder.</param>
/// <param name="IsFolder">True when the entry is a folder.</param>
internal sealed record DropboxObjectMetadata(
    string Id,
    string PathLower,
    string Name,
    string? Rev,
    long SizeBytes,
    DateTime? ServerModifiedUtc,
    string? ContentHash,
    bool IsFolder);
