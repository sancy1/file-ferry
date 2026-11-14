// filepath: src/ImageTextify.Core/Abstractions/ISpeechSegmenter.cs
// layer:    Contracts
// module:   ImageTextify.Core.Abstractions
// since:    Phase 5 Block 5.A.2
// purpose:  Contract for finding speech regions in a 16 kHz mono PCM WAV.
//           Silero VAD is the implementation. Empty regions is a normal
//           outcome (no speech in the audio).
//
// RELATIONSHIPS
//   Implements : n/a (interface)
//   Depends on : n/a (BCL only)
//   Used by    : ImageTextify.Platform.Speech.TranscriptionPipeline
//   See also   : ImageTextify.Core.Abstractions.ISpeechRecognizer
//
// NOTES
//   - The segmenter is mandatory. Whisper hallucinates on non-speech input;
//     the segmenter is the first defense layer (see the technology decision
//     document, Decision 5).
//   - Regions are non-overlapping and ordered by Start.

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// Finds speech regions in a PCM WAV.
/// </summary>
public interface ISpeechSegmenter
{
    /// <summary>
    /// Finds the speech regions in a WAV file.
    /// </summary>
    /// <param name="wavFilePath">The absolute WAV path. Not null.</param>
    /// <param name="cancellationToken">Cancels the detection.</param>
    /// <returns>The ordered regions. Never null; may be empty.</returns>
    Task<IReadOnlyList<SpeechRegion>> DetectAsync(string wavFilePath, CancellationToken cancellationToken);
}

/// <summary>
/// One speech region within the PCM WAV.
/// </summary>
/// <param name="Start">The region start.</param>
/// <param name="End">The region end. Greater than Start.</param>
public sealed record SpeechRegion(TimeSpan Start, TimeSpan End);