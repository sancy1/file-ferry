// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/CloudEntryPage.cs
// PURPOSE:  One page of a cloud listing plus an optional continuation
//           token. The token is opaque and provider-specific.
// RELATION: Returned by ICloudProvider.ListAsync. Consumed by the cloud
//           file picker for paginated browsing.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// A page of cloud listing entries.
/// </summary>
/// <param name="Entries">
/// The entries in this page, in provider-defined order. Never null;
/// may be empty.
/// </param>
/// <param name="ContinuationToken">
/// An opaque token for the next page, or null when this is the last
/// page. The caller passes it back unmodified.
/// </param>
public sealed record CloudEntryPage(
    IReadOnlyList<CloudEntry> Entries,
    string? ContinuationToken);
