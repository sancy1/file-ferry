// filepath: src/ImageTextify.Platform/Speech/WhisperNetRecognizer.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Speech
// since:    Phase 5 Block 5.D.2
// purpose:  Implements ISpeechRecognizer using Whisper.net 1.9.1 (GGML).
//           Given a 16 kHz mono PCM WAV and a model path, returns the
//           recognized segments with model-native timestamps.
//
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.ISpeechRecognizer
//   Depends on : Whisper.net (WhisperFactory, WhisperProcessorBuilder,
//                WhisperProcessor, SegmentData)
//   Used by    : ImageTextify.Platform.Speech.TranscriptionPipeline (via DI)
//   See also   : ImageTextify.Core.Abstractions.ISpeechRecognizer,
//                docs/ImageTextify/ImageTextify-Audio-Video-Technology-Decision.md
//
// NOTES
//   - The model path is supplied by the caller. In production, the
//     ModelStore resolves it from ModelManifest.WhisperSmallGgml via
//     IModelStore.EnsureAvailableAsync.
//   - Layer 2 of the five-layer hallucination defense: the builder sets
//     WithNoContext (no previous-text conditioning on long files),
//     WithTemperature(0) + WithTemperatureInc(0.2) for deterministic
//     decoding with a bounded fallback ramp, and the log-prob,
//     entropy, and no-speech thresholds. The specific numeric values
//     are the whisper.cpp defaults; their measured effect on this repo's
//     evaluation set is a 5.F concern (F-AV-052).
//   - Cancellation propagates to Whisper.net's ProcessAsync; on cancel
//     the process returns OperationCanceledException, which the pipeline
//     catches and maps to TranscriptionOutcome.Cancelled.
//   - An empty recognition is a normal outcome; the pipeline maps it to
//     TranscriptionOutcome.NoSpeechDetected.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;
using Whisper.net;

namespace ImageTextify.Platform.Speech;

/// <summary>
/// Transcribes a 16 kHz mono PCM WAV using Whisper.net (GGML).
/// </summary>
public sealed class WhisperNetRecognizer : ISpeechRecognizer
{
    private readonly string _modelPath;

    /// <summary>
    /// Constructs the recognizer.
    /// </summary>
    /// <param name="modelPath">The absolute path to the Whisper GGML model. Not null.</param>
    /// <exception cref="ArgumentNullException">Thrown when modelPath is null.</exception>
    public WhisperNetRecognizer(string modelPath)
    {
        ArgumentNullException.ThrowIfNull(modelPath);
        _modelPath = modelPath;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RecognizedSegment>> TranscribeAsync(
        string wavFilePath,
        SpeechRecognizerOptions options,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wavFilePath);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        using var factory = WhisperFactory.FromPath(_modelPath);
        var builder = factory.CreateBuilder()
            .WithNoContext()
            .WithTemperature(0.0f)
            .WithTemperatureInc(0.2f)
            .WithEntropyThreshold(2.4f)
            .WithLogProbThreshold(-1.0f)
            .WithNoSpeechThreshold(0.6f);

        if (string.IsNullOrEmpty(options.LanguageHint))
        {
            builder = builder.WithLanguageDetection();
        }
        else
        {
            builder = builder.WithLanguage(options.LanguageHint!);
        }

        using var processor = builder.Build();
        using var stream = File.OpenRead(wavFilePath);

        var results = new List<RecognizedSegment>();
        await foreach (var seg in processor.ProcessAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            var probability = (double?)seg.Probability;
            results.Add(new RecognizedSegment(
                seg.Start,
                seg.End,
                seg.Text ?? string.Empty,
                probability,
                seg.Language));
        }

        _ = progress; // Progress wiring is a 5.E concern.
        _ = CultureInfo.InvariantCulture;
        return results;
    }
}