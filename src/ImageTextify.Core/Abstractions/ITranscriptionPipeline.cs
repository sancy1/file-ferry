// filepath: src/ImageTextify.Core/Abstractions/ITranscriptionPipeline.cs
// layer:    Contracts
// module:   ImageTextify.Core.Abstractions
// since:    Phase 5 Block 5.A
// purpose:  Contract for the audio/video transcription pipeline. The
//           pipeline composes four ports (probe, decoder, segmenter,
//           recognizer) and returns a typed result. Expected failure
//           states are outcomes, not exceptions, so the UI maps outcome
//           to user text without exception parsing.
//
// RELATIONSHIPS
//   Implements : n/a (interface)
//   Depends on : n/a (BCL only)
//   Used by    : ImageTextify.Platform.Speech.TranscriptionPipeline,
//                FileHarvester.Tests.ImageTextify.TranscriptionPipelineTests
//   See also   : ImageTextify.Core.Abstractions.IImageOcrEngine,
//                ImageTextify.Core.Abstractions.IModelStore
//
// NOTES
//   - TranscriptionResult is a sealed record. Adding a parameter is a
//     breaking change. No external constructors exist as of Block 5.A.
//   - Every non-Completed outcome carries a UserMessage. The UI shows
//     UserMessage verbatim; it never inspects Outcome for text.
//   - Provenance records the model id, model SHA-256, runtime, and VAD
//     model, so a transcript can be explained after the fact.
//   - Invariants the pipeline enforces before every return:
//       1. Segment times are in order and within the document duration.
//       2. A Completed result has at least one segment.
//       3. A non-Completed result carries no document.
//       4. Cancelled leaves no output file and no partial state.
//       5. Temporary files live under the app-owned temp directory and
//          are removed on every exit path.

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// The audio/video transcription pipeline.
/// </summary>
public interface ITranscriptionPipeline
{
    /// <summary>
    /// Runs the pipeline against a request.
    /// </summary>
    /// <param name="request">The request. Not null.</param>
    /// <param name="progress">
    /// Reports progress measured in seconds of audio processed. May be
    /// null. The pipeline does not require a progress sink.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the run. The pipeline honors the token, terminates any
    /// child process, and leaves no partial output.
    /// </param>
    /// <returns>
    /// The typed outcome, the document (only when Completed), and a
    /// user-facing message for every other outcome.
    /// </returns>
    Task<TranscriptionResult> RunAsync(
        TranscriptionRequest request,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// The quality tier for a transcription. Maps to a Whisper model tier.
/// </summary>
public enum TranscriptionQuality
{
    /// <summary>Whisper base. Live transcription and very slow machines.</summary>
    Fast = 0,

    /// <summary>Whisper small. Default on CPU-only machines.</summary>
    Standard = 1,

    /// <summary>Whisper large-v3-turbo. Default with a GPU backend.</summary>
    High = 2,

    /// <summary>Whisper large-v3. Optional download for the hardest recordings.</summary>
    Maximum = 3,
}

/// <summary>
/// A request to transcribe one media file.
/// </summary>
/// <param name="FilePath">The source file path. Not null.</param>
/// <param name="AudioStreamIndex">
/// The audio track to extract when the container has multiple. Null uses
/// the default track.
/// </param>
/// <param name="LanguageHint">
/// An optional ISO language hint. Null means auto-detect.
/// </param>
/// <param name="Quality">The quality tier. Defaults to Standard.</param>
public sealed record TranscriptionRequest(
    string FilePath,
    int? AudioStreamIndex = null,
    string? LanguageHint = null,
    TranscriptionQuality Quality = TranscriptionQuality.Standard);

/// <summary>
/// The outcome of a pipeline run. Completed is the only outcome that
/// carries a document.
/// </summary>
public enum TranscriptionOutcome
{
    /// <summary>The run succeeded. A document is present.</summary>
    Completed = 0,

    /// <summary>No speech was found after VAD. No document.</summary>
    NoSpeechDetected = 1,

    /// <summary>The container or codec is not supported. No document.</summary>
    UnsupportedMedia = 2,

    /// <summary>The file has no audio track. No document.</summary>
    NoAudioTrack = 3,

    /// <summary>The decoder tool is not available. No document.</summary>
    ToolMissing = 4,

    /// <summary>The speech model is not available. No document.</summary>
    ModelMissing = 5,

    /// <summary>The caller cancelled. No document.</summary>
    Cancelled = 6,

    /// <summary>An unexpected failure occurred. No document.</summary>
    Failed = 7,
}

/// <summary>
/// The result of a pipeline run.
/// </summary>
/// <param name="Outcome">The typed outcome.</param>
/// <param name="Document">The transcript. Non-null only when Outcome is Completed.</param>
/// <param name="UserMessage">The user-facing message. Non-null for every non-Completed outcome.</param>
public sealed record TranscriptionResult(
    TranscriptionOutcome Outcome,
    TranscriptDocument? Document,
    string? UserMessage);

/// <summary>
/// A finished transcript with per-segment data and provenance.
/// </summary>
/// <param name="SourceFileName">The original file name.</param>
/// <param name="Duration">The media duration.</param>
/// <param name="Language">The detected language ISO code, or null when detection was skipped.</param>
/// <param name="LanguageConfidence">The language detection confidence in [0,1], or null.</param>
/// <param name="Segments">The transcript segments, ordered by Start. Never null.</param>
/// <param name="Warnings">The quality warnings. Never null; empty when none apply.</param>
/// <param name="Provenance">The engine provenance for reproducibility.</param>
public sealed record TranscriptDocument(
    string SourceFileName,
    TimeSpan Duration,
    string? Language,
    double? LanguageConfidence,
    IReadOnlyList<TranscriptSegment> Segments,
    IReadOnlyList<TranscriptionWarning> Warnings,
    EngineProvenance Provenance);

/// <summary>
/// Per-segment flags. A flags enumeration so callers can test combinations.
/// </summary>
[Flags]
public enum TranscriptSegmentMarkers
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>The confidence is below the evaluation-set threshold.</summary>
    LowConfidence = 1,

    /// <summary>The segment was suppressed by the deterministic hallucination filter.</summary>
    Suppressed = 2,

    /// <summary>The segment's no-speech probability was high.</summary>
    NoSpeechLikely = 4,
}

/// <summary>
/// A quality warning or a suppression notice.
/// </summary>
/// <param name="Code">A stable machine code, for example "low-level", "clipping", "suppressed".</param>
/// <param name="Message">A user-facing explanation.</param>
public sealed record TranscriptionWarning(string Code, string Message);

/// <summary>
/// Engine provenance for reproducibility.
/// </summary>
/// <param name="ModelId">The model id, for example "whisper-small".</param>
/// <param name="ModelSha256Hex">The model file SHA-256 in lowercase hex.</param>
/// <param name="Runtime">The runtime name, for example "whisper.net-1.9.1".</param>
/// <param name="VadModelId">The VAD model id, or null when VAD was disabled.</param>
public sealed record EngineProvenance(
    string ModelId,
    string ModelSha256Hex,
    string Runtime,
    string? VadModelId);

/// <summary>
/// A progress update measured in seconds of audio processed.
/// </summary>
/// <param name="ProcessedSeconds">The audio seconds processed so far.</param>
/// <param name="TotalSeconds">The total audio seconds, or null when not yet known.</param>
/// <param name="EstimatedRemaining">The estimated remaining time, or null when not yet known.</param>
public sealed record TranscriptionProgress(
    double ProcessedSeconds,
    double? TotalSeconds,
    TimeSpan? EstimatedRemaining);
