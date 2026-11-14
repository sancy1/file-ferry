// filepath: src/ImageTextify.Core/Abstractions/ISpeechRecognizer.cs
// layer:    Contracts
// module:   ImageTextify.Core.Abstractions
// since:    Phase 5 Block 5.A.2
// purpose:  Contract for running the speech model against one 16 kHz mono
//           PCM WAV and returning recognized segments. The recognizer port
//           is what makes Whisper.net swap-replaceable at a later gate.
//
// RELATIONSHIPS
//   Implements : n/a (interface)
//   Depends on : n/a (BCL only)
//   Used by    : ImageTextify.Platform.Speech.TranscriptionPipeline
//   See also   : ImageTextify.Core.Abstractions.ISpeechSegmenter,
//                ImageTextify.Core.Abstractions.TranscriptionQuality
//
// NOTES
//   - The recognizer receives the whole WAV. The implementation handles its
//     own windowing against the Whisper 30-second encoder window.
//   - A missing model file maps to TranscriptionOutcome.ModelMissing at the
//     pipeline layer.
//   - Segments carry the model-native timestamps. The pipeline may offset
//     them when the recognizer is called per-region.

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// Runs the speech model against a WAV and returns recognized segments.
/// </summary>
public interface ISpeechRecognizer
{
    /// <summary>
    /// Transcribes the WAV file.
    /// </summary>
    /// <param name="wavFilePath">The absolute WAV path. Not null.</param>
    /// <param name="options">The recognizer options. Not null.</param>
    /// <param name="progress">The progress reporter, may be null.</param>
    /// <param name="cancellationToken">Cancels the run; no partial output is left.</param>
    /// <returns>The recognized segments. Never null; may be empty.</returns>
    Task<IReadOnlyList<RecognizedSegment>> TranscribeAsync(
        string wavFilePath,
        SpeechRecognizerOptions options,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// The recognizer options.
/// </summary>
/// <param name="LanguageHint">An optional ISO language hint, or null for auto-detect.</param>
/// <param name="Quality">The quality tier, mapping to the model tier.</param>
public sealed record SpeechRecognizerOptions(
    string? LanguageHint,
    TranscriptionQuality Quality);

/// <summary>
/// One recognized segment with model-native timestamps.
/// </summary>
/// <param name="Start">The segment start.</param>
/// <param name="End">The segment end. Greater than Start.</param>
/// <param name="Text">The recognized text. Never null; may be empty.</param>
/// <param name="Probability">The segment probability in [0,1], or null.</param>
/// <param name="Language">The detected language, or null.</param>
public sealed record RecognizedSegment(
    TimeSpan Start,
    TimeSpan End,
    string Text,
    double? Probability,
    string? Language);