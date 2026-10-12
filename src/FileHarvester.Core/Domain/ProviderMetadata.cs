// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/ProviderMetadata.cs
// PURPOSE:  Metadata for a cloud object: size, last-modified, content
//           type, ETag, and a provider-specific extra dictionary.
// RELATION: Returned by ICloudProvider.GetMetadataAsync. Consumed by
//           CloudContentReader and by CloudPathSearchService.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// Metadata for a cloud object.
/// </summary>
/// <param name="SizeBytes">Size in bytes.</param>
/// <param name="LastModifiedUtc">Last modified timestamp in UTC, or null.</param>
/// <param name="ContentType">MIME type, or null.</param>
/// <param name="ETag">Provider ETag, or null.</param>
/// <param name="Extra">
/// Provider-specific extra fields. Never null; may be empty.
/// </param>
public sealed record ProviderMetadata(
    long SizeBytes,
    DateTime? LastModifiedUtc,
    string? ContentType,
    string? ETag,
    IReadOnlyDictionary<string, string> Extra);
