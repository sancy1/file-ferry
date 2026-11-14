// ============================================================================
// FILE:     src/ImageTextify.Core/Domain/ImageTextifySourceFile.cs
// PURPOSE:  One queued input file. Carries the absolute path, the
//           display name, the category, the file size, and the
//           duration when the input is audio or video.
// RELATION: Consumed by the queue, validation, per-file processing, and
//           by ImageTextifyResult as the Source reference.
// ============================================================================

namespace ImageTextify.Core.Domain;

/// <summary>
/// One queued ImageTextify input file.
/// </summary>
/// <param name="FullPath">
/// The absolute path of the source file. Never null or empty. Not safe
/// to log without redaction; log <see cref="DisplayName"/> instead, or
/// pass the path through the redaction helper in the Diagnostics
/// namespace.
/// </param>
/// <param name="DisplayName">
/// The file name only, without directory components. Safe to log and
/// to display. Never null or empty.
/// </param>
/// <param name="Kind">The category of the source file.</param>
/// <param name="SizeBytes">
/// The file size in bytes. Non-negative. Zero is permitted for an
/// empty file that will be reported as a validation warning later.
/// </param>
/// <param name="Duration">
/// The duration of an audio or video file, when known. Null for images
/// and PDFs. Populated by the media probe before transcription.
/// </param>
public sealed record ImageTextifySourceFile(
    string FullPath,
    string DisplayName,
    ExtractionKind Kind,
    long SizeBytes,
    TimeSpan? Duration);