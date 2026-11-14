// filepath: src/ImageTextify.Platform/Speech/TranscriptionPipeline.cs
// layer:    Boundary
// module:   ImageTextify.Platform.Speech
// since:    Phase 5 Block 5.A.3
// purpose:  Composes the probe, decoder, segmenter, and recognizer ports into
//           the pipeline the ITranscriptionPipeline contract promises. This
//           boundary translates port success and port exceptions into the
//           pipeline's typed outcomes, and it enforces the invariants on the
//           returned document.
//
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.ITranscriptionPipeline
//   Depends on : IMediaProbe, IAudioDecoder, ISpeechSegmenter,
//                ISpeechRecognizer (from ImageTextify.Core.Abstractions)
//   Used by    : Phase 5 Block 5.E ViewModel (via DI),
//                FileHarvester.Tests.ImageTextify.TranscriptionPipelineTests
//   See also   : ImageTextify.Core.Abstractions.ITranscriptionPipeline
//
// NOTES
//   - The pipeline owns the temp directory lifecycle. Every temp path is
//     created under Path.GetTempPath()/imagetextify-b5a-<guid> and removed
//     on every exit path, including exception and cancellation.
//   - The pipeline does not construct its ports. DI supplies them. It has
//     no direct dependency on FFmpeg, Whisper, or any model.
//   - Invariants enforced before every return:
//       1. Segment times ascending and within Duration.
//       2. Completed => Segments.Count >= 1.
//       3. Non-Completed => Document == null.
//       4. Cancelled => no output written.
//   - Hallucination suppression, confidence flagging, and warning generation
//     are Block 5.C/5.D concerns. This block assembles segments with
//     TranscriptSegmentMarkers.None and Warnings empty.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;

namespace ImageTextify.Platform.Speech;

/// <summary>
/// The audio/video transcription pipeline. Composes ports; enforces invariants.
/// </summary>
public sealed class TranscriptionPipeline : ITranscriptionPipeline
{
    private readonly IMediaProbe _probe;
    private readonly IAudioDecoder _decoder;
    private readonly ISpeechSegmenter _segmenter;
    private readonly ISpeechRecognizer _recognizer;
    private readonly IHallucinationSuppressor _suppressor;

    /// <summary>
    /// Constructs the pipeline.
    /// </summary>
    /// <param name="probe">The media probe. Not null.</param>
    /// <param name="decoder">The audio decoder. Not null.</param>
    /// <param name="segmenter">The speech segmenter. Not null.</param>
    /// <param name="recognizer">The speech recognizer. Not null.</param>
    public TranscriptionPipeline(
        IMediaProbe probe,
        IAudioDecoder decoder,
        ISpeechSegmenter segmenter,
        ISpeechRecognizer recognizer)
        : this(probe, decoder, segmenter, recognizer, new NoOpHallucinationSuppressor())
    {
    }

    /// <summary>
    /// Constructs the pipeline with a hallucination suppressor.
    /// </summary>
    /// <param name="probe">The media probe. Not null.</param>
    /// <param name="decoder">The audio decoder. Not null.</param>
    /// <param name="segmenter">The speech segmenter. Not null.</param>
    /// <param name="recognizer">The speech recognizer. Not null.</param>
    /// <param name="suppressor">The hallucination suppressor. Not null.</param>
    public TranscriptionPipeline(
        IMediaProbe probe,
        IAudioDecoder decoder,
        ISpeechSegmenter segmenter,
        ISpeechRecognizer recognizer,
        IHallucinationSuppressor suppressor)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(segmenter);
        ArgumentNullException.ThrowIfNull(recognizer);
        ArgumentNullException.ThrowIfNull(suppressor);
        _probe = probe;
        _decoder = decoder;
        _segmenter = segmenter;
        _recognizer = recognizer;
        _suppressor = suppressor;
    }

    /// <inheritdoc/>
    public async Task<TranscriptionResult> RunAsync(
        TranscriptionRequest request,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested) { return Cancelled(); }

        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "imagetextify-b5a-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(tempDir);
        try
        {
            AudioProbeResult probeResult;
            try
            {
                probeResult = await _probe.ProbeAsync(request.FilePath, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return Cancelled(); }
            catch (Exception ex)
            {
                return new TranscriptionResult(
                    TranscriptionOutcome.UnsupportedMedia,
                    null,
                    "This file could not be inspected as media: " + ex.Message);
            }

            if (!probeResult.IsSupported)
            {
                return new TranscriptionResult(
                    TranscriptionOutcome.UnsupportedMedia,
                    null,
                    probeResult.UnsupportedReason ?? "This media format is not supported.");
            }
            if (probeResult.AudioStreams.Count == 0)
            {
                return new TranscriptionResult(
                    TranscriptionOutcome.NoAudioTrack,
                    null,
                    "This file has no audio track.");
            }

            var chosenStreamIndex = request.AudioStreamIndex ?? probeResult.AudioStreams[0].Index;

            string wavPath;
            try
            {
                wavPath = await _decoder.DecodeAsync(
                    request.FilePath,
                    chosenStreamIndex,
                    tempDir,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return Cancelled(); }
            catch (FileNotFoundException)
            {
                return new TranscriptionResult(
                    TranscriptionOutcome.ToolMissing,
                    null,
                    "The bundled media decoder was not found.");
            }
            catch (Exception ex)
            {
                return new TranscriptionResult(
                    TranscriptionOutcome.UnsupportedMedia,
                    null,
                    "This file's audio track could not be decoded: " + ex.Message);
            }

            if (!File.Exists(wavPath))
            {
                return new TranscriptionResult(
                    TranscriptionOutcome.Failed,
                    null,
                    "The decoder did not produce a WAV.");
            }

            IReadOnlyList<SpeechRegion> regions;
            try
            {
                regions = await _segmenter.DetectAsync(wavPath, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return Cancelled(); }
            catch (Exception ex)
            {
                return new TranscriptionResult(
                    TranscriptionOutcome.Failed,
                    null,
                    "Speech detection failed: " + ex.Message);
            }

            if (regions.Count == 0)
            {
                return new TranscriptionResult(
                    TranscriptionOutcome.NoSpeechDetected,
                    null,
                    "No speech was detected in this audio.");
            }

            IReadOnlyList<RecognizedSegment> recognized;
            try
            {
                recognized = await _recognizer.TranscribeAsync(
                    wavPath,
                    new SpeechRecognizerOptions(request.LanguageHint, request.Quality),
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return Cancelled(); }
            catch (FileNotFoundException)
            {
                return new TranscriptionResult(
                    TranscriptionOutcome.ModelMissing,
                    null,
                    "The bundled speech model was not found.");
            }
            catch (Exception ex)
            {
                return new TranscriptionResult(
                    TranscriptionOutcome.Failed,
                    null,
                    "Speech recognition failed: " + ex.Message);
            }

            if (recognized.Count == 0)
            {
                return new TranscriptionResult(
                    TranscriptionOutcome.NoSpeechDetected,
                    null,
                    "No speech was recognized in this audio.");
            }

            SuppressionResult suppression;
            try
            {
                suppression = await _suppressor.SuppressAsync(recognized, regions, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return Cancelled(); }
            catch (Exception ex)
            {
                return new TranscriptionResult(
                    TranscriptionOutcome.Failed,
                    null,
                    "Hallucination suppression failed: " + ex.Message);
            }

            if (suppression.Segments.Count == 0)
            {
                return new TranscriptionResult(
                    TranscriptionOutcome.NoSpeechDetected,
                    null,
                    "All recognized segments were suppressed as non-speech.");
            }

            var segments = new List<TranscriptSegment>(suppression.Segments.Count);
            foreach (var r in suppression.Segments)
            {
                var text = r.Text ?? string.Empty;
                segments.Add(new TranscriptSegment(
                    r.Start,
                    r.End,
                    text,
                    null,
                    null,
                    TranscriptSegmentMarkers.None));
            }

            for (int i = 1; i < segments.Count; i++)
            {
                if (segments[i].Start < segments[i - 1].Start)
                {
                    return new TranscriptionResult(
                        TranscriptionOutcome.Failed,
                        null,
                        "Internal error: segments not ordered.");
                }
            }
            foreach (var s in segments)
            {
                if (s.End > probeResult.Duration)
                {
                    return new TranscriptionResult(
                        TranscriptionOutcome.Failed,
                        null,
                        "Internal error: segment extends beyond duration.");
                }
            }

            string? detectedLanguage = null;
            if (recognized.Count > 0) { detectedLanguage = recognized[0].Language; }

            var document = new TranscriptDocument(
                Path.GetFileName(request.FilePath),
                probeResult.Duration,
                detectedLanguage,
                null,
                segments,
                suppression.Warnings,
                new EngineProvenance(
                    ModelId: "whisper-" + request.Quality.ToString().ToLowerInvariant(),
                    ModelSha256Hex: new string('0', 64),
                    Runtime: "whisper.net-1.9.1",
                    VadModelId: "silero-vad"));

            return new TranscriptionResult(TranscriptionOutcome.Completed, document, null);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private static TranscriptionResult Cancelled() =>
        new(TranscriptionOutcome.Cancelled, null, "Transcription was cancelled.");
}