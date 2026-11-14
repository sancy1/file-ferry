// filepath: src/ImageTextify.Core/Abstractions/IHallucinationSuppressor.cs
// layer:    Contracts
// module:   ImageTextify.Core.Abstractions
// since:    Phase 5 Block 5.D.3
// purpose:  Contract for the deterministic suppression of hallucinated
//           recognition segments. Layer 3 of the five-layer defense in
//           Decision 5 of the technology decision document.
//
// RELATIONSHIPS
//   Implements : n/a (interface)
//   Depends on : RecognizedSegment, SpeechRegion, TranscriptionWarning
//   Used by    : ImageTextify.Platform.Speech.TranscriptionPipeline
//   See also   : ImageTextify.Core.Abstractions.ISpeechRecognizer,
//                ImageTextify.Core.Abstractions.ISpeechSegmenter
//
// NOTES
//   - Suppression is deterministic: no ML, no randomness, no external
//     data. Every rule is a comparison against known inputs.
//   - Nothing is dropped silently. Every suppressed segment increments a
//     counter that appears in the returned Warnings list. The decision
//     record requires: "Nothing is dropped silently."

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// Suppresses hallucinated segments from a recognition result.
/// </summary>
public interface IHallucinationSuppressor
{
    /// <summary>
    /// Applies deterministic suppression rules to the recognized segments.
    /// </summary>
    /// <param name="segments">The recognized segments. Not null.</param>
    /// <param name="speechRegions">The detected speech regions. Not null.</param>
    /// <param name="cancellationToken">Cancels the suppression.</param>
    /// <returns>
    /// The retained segments and any warnings for the user. Never null.
    /// </returns>
    Task<SuppressionResult> SuppressAsync(
        IReadOnlyList<RecognizedSegment> segments,
        IReadOnlyList<SpeechRegion> speechRegions,
        CancellationToken cancellationToken);
}

/// <summary>
/// The result of a suppression pass.
/// </summary>
/// <param name="Segments">The retained segments, in original order. Never null.</param>
/// <param name="Warnings">The suppression and quality warnings. Never null.</param>
public sealed record SuppressionResult(
    IReadOnlyList<RecognizedSegment> Segments,
    IReadOnlyList<TranscriptionWarning> Warnings);