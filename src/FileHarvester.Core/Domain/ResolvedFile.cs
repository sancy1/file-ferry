// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/ResolvedFile.cs
// PURPOSE:  Domain record representing a FileRequest after the resolver has
//           located it on disk. Carries the absolute path plus metadata the
//           resolver read, and a confidence marker describing how certain
//           the match is.
// RELATION: Produced by IFileResolver (Phase 1.1) from a FileRequest.
//           Consumed by IContentReader (Phase 1.2) which reads the bytes at
//           AbsolutePath. The confidence marker flows through to the UI to
//           let the user see whether a path was an exact match or the
//           result of a whole-PC search.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// A file located on disk, with the metadata the resolver gathered.
/// </summary>
/// <param name="Request">
/// The original FileRequest this was resolved from. Preserves the user's
/// input text and original ordering.
/// </param>
/// <param name="AbsolutePath">
/// The full, canonical path to the file. Always an absolute path with
/// the OS-native separator. The resolver guarantees this file exists at
/// the moment of resolution.
/// </param>
/// <param name="SizeBytes">
/// File size at the moment of resolution, in bytes. May be stale if the
/// file changes between resolution and read; the reader reports the
/// actual size it read.
/// </param>
/// <param name="LastModifiedUtc">
/// File's last-modified timestamp in UTC. Used by the ambiguity dialog
/// (Phase 5.2) to help the user pick among multiple matches.
/// </param>
/// <param name="Confidence">
/// How certain the resolver is that this is the file the user meant.
/// </param>
public sealed record ResolvedFile(
    FileRequest Request,
    string AbsolutePath,
    long SizeBytes,
    DateTime LastModifiedUtc,
    ResolutionConfidence Confidence);

/// <summary>
/// Describes how the resolver found this file.
/// </summary>
public enum ResolutionConfidence
{
    /// <summary>
    /// The user provided an absolute path and the file exists. Highest
    /// confidence, no search needed.
    /// </summary>
    Exact = 0,

    /// <summary>
    /// The user provided a relative path and it resolved against a
    /// known base directory (HintedDirectory or a standard location).
    /// </summary>
    Relative = 1,

    /// <summary>
    /// The user provided a bare filename and the resolver found exactly
    /// one match through a PC-wide search.
    /// </summary>
    Searched = 2,

    /// <summary>
    /// The user provided a bare filename and the resolver found more
    /// than one match. The UI prompts the user to pick (Phase 5.2). One
    /// ResolvedFile per match is produced with this confidence.
    /// </summary>
    Ambiguous = 3,
}
