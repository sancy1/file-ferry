// filepath: src/ImageTextify.Platform/Speech/HallucinationSuppressor.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Speech
// since:    Phase 5 Block 5.D.3
// purpose:  Implements IHallucinationSuppressor with three deterministic
//           rules: drop segments outside detected speech; drop repetition
//           loops of three or more; drop known hallucination phrases only
//           when outside speech or with low probability. Every drop is
//           counted in a warning. Nothing is silent.
//
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.IHallucinationSuppressor
//   Depends on : RecognizedSegment, SpeechRegion, TranscriptionWarning
//   Used by    : ImageTextify.Platform.Speech.TranscriptionPipeline
//   See also   : ImageTextify.Platform.Speech.NoOpHallucinationSuppressor
//
// NOTES
//   - Layer 3 of the five-layer defense (Decision 5 of the technology
//     decision document).
//   - The KnownHallucinationPhrases set is hand-curated from common
//     Whisper reports and the arxiv 2501.11378v1 study. It is NOT
//     measured against this repo's evaluation set; Block 5.F calibrates
//     it. F-AV-055 records this.
//   - The repetition loop rule keeps the first occurrence of a repeated
//     phrase; only the second and later consecutive duplicates are
//     dropped. This avoids over-suppression of legitimate repetition in
//     normal speech (e.g., "no, no, no" is allowed; the same sentence
//     four times is not).
//   - The known-phrase rule fires only when the segment is inside speech
//     AND probability is below LowProbabilityThreshold (0.3). A segment
//     inside speech with probability 0.9 that matches "thank you" is
//     retained, because "thank you" is a real phrase in real speech.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;

namespace ImageTextify.Platform.Speech;

/// <summary>
/// Deterministic hallucination suppressor.
/// </summary>
public sealed class HallucinationSuppressor : IHallucinationSuppressor
{
    private const double LowProbabilityThreshold = 0.3;
    private const int RepetitionRunThreshold = 3;

    private static readonly HashSet<string> KnownHallucinationPhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        "thank you",
        "thanks for watching",
        "thank you for watching",
        "please subscribe",
        "subscribe to my channel",
        "like and subscribe",
        "see you next time",
        "bye",
        "bye bye",
        "you",
    };

    /// <inheritdoc/>
    public Task<SuppressionResult> SuppressAsync(
        IReadOnlyList<RecognizedSegment> segments,
        IReadOnlyList<SpeechRegion> speechRegions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(speechRegions);
        cancellationToken.ThrowIfCancellationRequested();

        var retained = new List<RecognizedSegment>(segments.Count);
        int outsideSpeech = 0;
        int repetitionLoops = 0;
        int knownPhrases = 0;

        int lastRetainedIndex = -1;
        string? lastRetainedText = null;
        int runLength = 0;

        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            var midpoint = s.Start + TimeSpan.FromTicks((s.End - s.Start).Ticks / 2);
            var insideSpeech = IsInsideSpeech(midpoint, speechRegions);

            if (!insideSpeech)
            {
                outsideSpeech++;
                continue;
            }

            var text = (s.Text ?? string.Empty).Trim();

            // Repetition loop: same text as previous retained segment, run length >= threshold.
            if (lastRetainedText is not null && string.Equals(text, lastRetainedText, StringComparison.Ordinal))
            {
                runLength++;
                if (runLength >= RepetitionRunThreshold)
                {
                    repetitionLoops++;
                    continue;
                }
            }
            else
            {
                runLength = 1;
            }

            // Known hallucination phrase, only if low probability.
            if (KnownHallucinationPhrases.Contains(text) && (s.Probability ?? 0.0) < LowProbabilityThreshold)
            {
                knownPhrases++;
                continue;
            }

            retained.Add(s);
            lastRetainedIndex = i;
            lastRetainedText = text;
            _ = lastRetainedIndex;
        }

        var warnings = new List<TranscriptionWarning>();
        if (outsideSpeech > 0)
        {
            warnings.Add(new TranscriptionWarning(
                "suppressed-outside-speech",
                outsideSpeech.ToString(System.Globalization.CultureInfo.InvariantCulture) + " passages suppressed: outside detected speech."));
        }
        if (repetitionLoops > 0)
        {
            warnings.Add(new TranscriptionWarning(
                "suppressed-repetition",
                repetitionLoops.ToString(System.Globalization.CultureInfo.InvariantCulture) + " passages suppressed: repetition loop."));
        }
        if (knownPhrases > 0)
        {
            warnings.Add(new TranscriptionWarning(
                "suppressed-known-phrase",
                knownPhrases.ToString(System.Globalization.CultureInfo.InvariantCulture) + " passages suppressed: known hallucination phrase."));
        }

        return Task.FromResult(new SuppressionResult(retained, warnings));
    }

    private static bool IsInsideSpeech(TimeSpan point, IReadOnlyList<SpeechRegion> regions)
    {
        foreach (var r in regions)
        {
            if (point >= r.Start && point <= r.End) { return true; }
        }
        return false;
    }
}