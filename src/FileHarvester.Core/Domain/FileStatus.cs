// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/FileStatus.cs
// PURPOSE:  Lifecycle states for one file as it flows through the harvest
//           pipeline. Drives the status pill shown next to each row in the
//           Resolved Files panel (Phase 3.2).
// RELATION: Produced by the UI's Progress ViewModel (Phase 3.2) as the
//           resolver and reader report progress. Consumed only by the UI;
//           no domain logic depends on this enum.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// Where one file is in the harvest pipeline.
/// </summary>
public enum FileStatus
{
    /// <summary>
    /// Waiting for the resolver to look at this entry. Initial state.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The resolver found the file on disk. Metadata is available.
    /// </summary>
    Found = 1,

    /// <summary>
    /// The resolver could not find the file anywhere. Not retryable.
    /// </summary>
    NotFound = 2,

    /// <summary>
    /// The bare filename matched multiple files on disk. The ambiguity
    /// dialog (Phase 5.2) will ask the user which one they meant.
    /// </summary>
    Ambiguous = 3,

    /// <summary>
    /// The file was found but the reader failed to load its contents
    /// (permission denied, circuit open, timeout). The bundle will
    /// include a placeholder note.
    /// </summary>
    ReadError = 4,

    /// <summary>
    /// The file was deliberately skipped (binary content, above size
    /// limit, on the skip list). The bundle will include a note.
    /// </summary>
    Skipped = 5,
}
