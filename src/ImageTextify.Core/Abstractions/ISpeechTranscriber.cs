// ============================================================================
// FILE:     src/ImageTextify.Core/Abstractions/ISpeechTranscriber.cs
// PURPOSE:  Contract for file or stream speech transcription. The
//           input is 16 kHz mono PCM WAV produced by IMediaPreparer.
//           No speaker identity is produced in the first release.
// RELATION: Implemented in Phase 5 with a local Whisper-family model.
//           Consumed by the audio and video pipelines.
// ============================================================================

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// Transcribes 16 kHz mono PCM audio.
/// </summary>
public interface ISpeechTranscriber
{
    /// <summary>
    /// Transcribes a prepared audio stream.
    /// </summary>
    /// <param name="audioStream">
    /// The 16 kHz mono PCM WAV bytes. The caller owns the stream.
    /// </param>
    /// <param name="options">
    /// Transcription options. Not null.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the transcription. The transcriber honors the token
    /// promptly.
    /// </param>
    /// <returns>
    /// The transcript, its timestamped segments, and any quality
    /// warnings.
    /// </returns>
    Task<TranscriptResult> TranscribeAsync(
        Stream audioStream,
        TranscriptionOptions options,
        CancellationToken cancellationToken);
}

/// <summary>
/// Options controlling transcription.
/// </summary>
/// <param name="Language">
/// The language hint for the recognizer, or null for auto-detection.
/// </param>
/// <param name="IncludeTimestamps">
/// When true, produce segment timestamps alongside the text. Default
/// true.
/// </param>
public sealed record TranscriptionOptions(
    string? Language = null,
    bool IncludeTimestamps = true);

/// <summary>
/// The result of a transcription.
/// </summary>
/// <param name="Text">
/// The full transcript. Never null; may be empty when no speech was
/// detected.
/// </param>
/// <param name="Segments">
/// The timestamped segments in order. Never null; may be empty when
/// the transcriber did not segment.
/// </param>
/// <param name="Warnings">
/// Quality warnings collected during transcription. Never null.
/// </param>
public sealed record TranscriptResult(
    string Text,
    IReadOnlyList<TranscriptSegment> Segments,
    IReadOnlyList<string> Warnings);

/// <summary>
/// One timestamped transcript segment.
/// </summary>
/// <param name="Start">The segment start relative to the audio.</param>
/// <param name="End">The segment end relative to the audio.</param>
/// <param name="Text">The segment text. Never null; may be empty.</param>
/// <param name="SpeakerLabel">
/// The speaker label. Always null in the first release. Speaker
/// separation and diarization are a separate, deferred milestone.
/// </param>
/// <param name="Confidence">
/// The confidence in [0,1], or null when the engine does not report one.
/// </param>
/// <param name="Flags">
/// Per-segment flags set by the pipeline (low confidence, suppressed, no-speech).
/// </param>
public sealed record TranscriptSegment(
    TimeSpan Start,
    TimeSpan End,
    string Text,
    string? SpeakerLabel,
    double? Confidence,
    TranscriptSegmentMarkers Flags);
