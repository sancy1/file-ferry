// filepath: src/ImageTextify.Platform/Speech/WhisperNetSileroSegmenter.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Speech
// since:    Phase 5 Block 5.C
// purpose:  Implements ISpeechSegmenter using Whisper.net's Silero VAD
//           path (WhisperVadFactory). Given a 16 kHz mono PCM WAV,
//           returns the ordered speech regions detected by Silero.
//
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.ISpeechSegmenter
//   Depends on : Whisper.net (WhisperVadFactory, VadSegmentData),
//                SpeechRegion
//   Used by    : ImageTextify.Platform.Speech.TranscriptionPipeline (via DI)
//   See also   : ImageTextify.Core.Abstractions.ISpeechSegmenter,
//                docs/ImageTextify/ImageTextify-Audio-Video-Technology-Decision.md
//
// NOTES
//   - The Silero VAD model is a separate GGML file from the Whisper model.
//     Gate 0.B verified the WhisperGgmlDownloader.Default.GetGgmlSileroVadModelAsync
//     path produces a working VAD model at 1.9.1.
//   - The segmenter is mandatory. Whisper hallucinates on non-speech input;
//     this is layer 1 of the five-layer defense in Decision 5 of the
//     technology decision document.
//   - The VAD model path is supplied by the caller. The ModelStore (via
//     IModelStore.EnsureAvailableAsync) resolves it once the manifest entry
//     is added in Block 5.D.
//   - An empty result is normal: the audio contained no speech regions.
//     The pipeline maps that to TranscriptionOutcome.NoSpeechDetected.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;
using Whisper.net;

namespace ImageTextify.Platform.Speech;

/// <summary>
/// Finds speech regions using Silero VAD via Whisper.net.
/// </summary>
public sealed class WhisperNetSileroSegmenter : ISpeechSegmenter
{
    private readonly string _vadModelPath;

    /// <summary>
    /// Constructs the segmenter.
    /// </summary>
    /// <param name="vadModelPath">
    /// The absolute path to the Silero VAD GGML model file. Not null.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when vadModelPath is null.</exception>
    public WhisperNetSileroSegmenter(string vadModelPath)
    {
        ArgumentNullException.ThrowIfNull(vadModelPath);
        _vadModelPath = vadModelPath;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SpeechRegion>> DetectAsync(string wavFilePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wavFilePath);
        cancellationToken.ThrowIfCancellationRequested();

        using var vadFactory = WhisperVadFactory.FromPath(_vadModelPath);
        using var vad = vadFactory.CreateBuilder().Build();
        using var stream = File.OpenRead(wavFilePath);

        var detected = await vad.DetectSpeechAsync(stream, cancellationToken).ConfigureAwait(false);

        var regions = new List<SpeechRegion>(detected.Count);
        foreach (var r in detected)
        {
            regions.Add(new SpeechRegion(r.Start, r.End));
        }
        return regions;
    }
}