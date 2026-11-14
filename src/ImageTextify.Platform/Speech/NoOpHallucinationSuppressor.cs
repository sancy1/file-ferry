// filepath: src/ImageTextify.Platform/Speech/NoOpHallucinationSuppressor.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Speech
// since:    Phase 5 Block 5.D.3
// purpose:  A pass-through IHallucinationSuppressor. Returns the segments
//           unchanged and an empty warnings list. Exists so the pipeline's
//           4-arg constructor (used by tests and by non-production
//           composition) can satisfy the interface without a suppressor.
//
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.IHallucinationSuppressor
//   Depends on : RecognizedSegment, SpeechRegion, TranscriptionWarning
//   Used by    : ImageTextify.Platform.Speech.TranscriptionPipeline
//   See also   : ImageTextify.Platform.Speech.HallucinationSuppressor
//
// NOTES
//   - Not to be used in production. The real suppressor is
//     HallucinationSuppressor. This class exists to keep the pipeline's
//     4-arg constructor signature stable for the existing 16 tests and
//     to keep the pipeline testable without the suppressor.
//   - F-AV-056 records this concern: the 4-arg pipeline constructor
//     creates a hidden no-op. DI in production must supply the real
//     suppressor via the 5-arg constructor overload.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;

namespace ImageTextify.Platform.Speech;

/// <summary>
/// A pass-through suppressor. Returns segments unchanged.
/// </summary>
public sealed class NoOpHallucinationSuppressor : IHallucinationSuppressor
{
    /// <inheritdoc/>
    public Task<SuppressionResult> SuppressAsync(
        IReadOnlyList<RecognizedSegment> segments,
        IReadOnlyList<SpeechRegion> speechRegions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SuppressionResult(segments, System.Array.Empty<TranscriptionWarning>()));
    }
}