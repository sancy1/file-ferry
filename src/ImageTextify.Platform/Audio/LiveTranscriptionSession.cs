// filepath: src/ImageTextify.Platform/Audio/LiveTranscriptionSession.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Audio
// since:    Phase 10 Block 10.C.1
// purpose:  Per-session implementation of ILiveTranscriptionSession. This
//           block delivers the class skeleton: all four events, State, and
//           disposal. The run loop, interim timer, final transcription, and
//           suppressor wiring are added in Block 10.C.3.
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.ILiveTranscriptionSession
//   Depends on : IAudioCaptureService, IStreamingSpeechSegmenter,
//                ISpeechRecognizer, IHallucinationSuppressor
//   Used by    : ImageTextify.Platform.Audio.LiveTranscriptionService
//   See also   : ImageTextify.Core.Abstractions.ILiveTranscriptionService,
//                ADR-002
// NOTES
//   - Internal. The public surface is the ILiveTranscriptionSession the
//     factory returns; the concrete class is a Phase 10.C detail.
//   - The four dependency fields are held so Block 10.C.3 can wire them.
//     They are exposed through internal read-only properties so the
//     compiler and analyzer see them as used (CA1823 avoidance under
//     TreatWarningsAsErrors).
//   - StopAsync and DisposeAsync are no-ops in this block. The capture
//     session passed in the constructor is owned by the caller until
//     Block 10.C.3 takes ownership.

using System;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;
using System.IO;

namespace ImageTextify.Platform.Audio;

internal sealed class LiveTranscriptionSession : ILiveTranscriptionSession
{
    private readonly IAudioCaptureService _captureService;
    private readonly IStreamingSpeechSegmenter _segmenter;
    private readonly ISpeechRecognizer _recognizer;
    private readonly IHallucinationSuppressor _suppressor;
    private readonly IAudioCaptureSession _capture;
    private readonly Stream _audioStream;
    private readonly string? _language;
    private Task _runTask = Task.CompletedTask;
    private string? _tempDir;
    private LiveTranscriptionState _state;

    internal LiveTranscriptionSession(
        IAudioCaptureService captureService,
        IStreamingSpeechSegmenter segmenter,
        ISpeechRecognizer recognizer,
        IHallucinationSuppressor suppressor,
        IAudioCaptureSession capture,
        Stream audioStream,
        string? language,
        LiveTranscriptionState initialState)
    {
        ArgumentNullException.ThrowIfNull(captureService);
        ArgumentNullException.ThrowIfNull(segmenter);
        ArgumentNullException.ThrowIfNull(recognizer);
        ArgumentNullException.ThrowIfNull(suppressor);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(audioStream);
        _captureService = captureService;
        _segmenter = segmenter;
        _recognizer = recognizer;
        _suppressor = suppressor;
        _capture = capture;
        _audioStream = audioStream;
        _language = language;
        _state = initialState;
    }

    internal IAudioCaptureService CaptureService => _captureService;
    internal IStreamingSpeechSegmenter Segmenter => _segmenter;
    internal ISpeechRecognizer Recognizer => _recognizer;
    internal IHallucinationSuppressor Suppressor => _suppressor;
    internal IAudioCaptureSession Capture => _capture;
    internal string? Language => _language;
    internal string? TempDir => _tempDir;
    internal Task RunTask => _runTask;

    /// <inheritdoc/>
    public event EventHandler<InterimTextReceivedEventArgs>? InterimTextReceived;

    /// <inheritdoc/>
    public event EventHandler<FinalTextReceivedEventArgs>? FinalTextReceived;

    /// <inheritdoc/>
    public event EventHandler<SegmentReceivedEventArgs>? SegmentReceived;

    /// <inheritdoc/>
    public event EventHandler<QualityWarningReceivedEventArgs>? QualityWarningReceived;

    /// <inheritdoc/>
    public event EventHandler<TranscriptionActivityEventArgs>? TranscriptionActivityChanged;

    /// <inheritdoc/>
    public LiveTranscriptionState State => _state;

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _state = LiveTranscriptionState.Stopping;
        _state = LiveTranscriptionState.Idle;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _state = LiveTranscriptionState.Idle;
        try { await _segmenter.DisposeAsync().ConfigureAwait(false); } catch { }
        try { await _capture.DisposeAsync().ConfigureAwait(false); } catch { }
    }

    internal void SetState(LiveTranscriptionState state) => _state = state;

    internal void RaiseInterim(string text) =>
        InterimTextReceived?.Invoke(this, new InterimTextReceivedEventArgs(text));

    internal void RaiseFinal(string text) =>
        FinalTextReceived?.Invoke(this, new FinalTextReceivedEventArgs(text));

    internal void RaiseSegment(TranscriptSegment segment) =>
        SegmentReceived?.Invoke(this, new SegmentReceivedEventArgs(segment));

    internal void RaiseQualityWarning(string warning) =>
        QualityWarningReceived?.Invoke(this, new QualityWarningReceivedEventArgs(warning));

    internal void RaiseTranscriptionActivity(bool isTranscribing) =>
        TranscriptionActivityChanged?.Invoke(this, new TranscriptionActivityEventArgs(isTranscribing));

    internal void StartRun(CancellationToken cancellationToken)
    {
        _runTask = RunAsync(cancellationToken);
    }

        internal async Task RunAsync(CancellationToken cancellationToken)
    {
        _state = LiveTranscriptionState.Listening;
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "imagetextify-10c-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _segmenter.UtteranceStarted += OnUtteranceStarted;
        _segmenter.UtteranceEnded += OnUtteranceEnded;
        try
        {
            await _segmenter.RunAsync(_audioStream, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { _state = LiveTranscriptionState.Failed; }
        finally
        {
            _segmenter.UtteranceStarted -= OnUtteranceStarted;
            _segmenter.UtteranceEnded -= OnUtteranceEnded;
            if (_tempDir is not null)
            {
                try { Directory.Delete(_tempDir, recursive: true); } catch { }
                _tempDir = null;
            }
            if (_state != LiveTranscriptionState.Failed)
            {
                _state = LiveTranscriptionState.Stopping;
                _state = LiveTranscriptionState.Idle;
            }
        }
    }

    private void OnUtteranceStarted(object? sender, UtteranceStartedEventArgs e)
    {
        _ = _state;
    }

    private void OnUtteranceEnded(object? sender, UtteranceEndedEventArgs e)
    {
        _ = _state;
        if (_tempDir is null) { return; }
        var wavPath = Path.Combine(_tempDir, "utterance-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            var conditioned = e.Samples.ToArray();
            UtteranceAudioConditioner.Condition(conditioned);
            WritePcm16Wav(wavPath, conditioned);
            RaiseTranscriptionActivity(true);
            var segments = _recognizer
                .TranscribeAsync(
                    wavPath,
                    new SpeechRecognizerOptions(_language, TranscriptionQuality.Fast),
                    progress: null,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            var regions = new SpeechRegion[] { new SpeechRegion(e.Start, e.End) };
            var suppression = _suppressor
                .SuppressAsync(segments, regions, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            foreach (var warning in suppression.Warnings)
            {
                RaiseQualityWarning(warning.Code + ": " + warning.Message);
            }
            var textBuilder = new System.Text.StringBuilder();
            foreach (var seg in suppression.Segments)
            {
                var segText = seg.Text;
                if (textBuilder.Length > 0
                    && !char.IsWhiteSpace(textBuilder[textBuilder.Length - 1])
                    && !string.IsNullOrEmpty(segText)
                    && !char.IsWhiteSpace(segText[0]))
                {
                    textBuilder.Append(' ');
                }
                textBuilder.Append(segText);
                var transcriptSegment = new TranscriptSegment(
                    e.Start + seg.Start,
                    e.Start + seg.End,
                    seg.Text,
                    SpeakerLabel: null,
                    seg.Probability,
                    TranscriptSegmentMarkers.None);
                RaiseSegment(transcriptSegment);
            }
            var finalText = textBuilder.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(finalText))
            {
                RaiseFinal(finalText);
            }
        }
        catch (Exception)
        {
            RaiseQualityWarning("transcription-failed");
        }
        finally
        {
            RaiseTranscriptionActivity(false);
            try { if (File.Exists(wavPath)) { File.Delete(wavPath); } } catch { }
        }
    }

    private static void WritePcm16Wav(string path, ReadOnlyMemory<float> samples)
    {
        var span = samples.Span;
        int sampleCount = span.Length;
        int dataBytes = sampleCount * 2;
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataBytes);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)1);
        bw.Write(16000);
        bw.Write(32000);
        bw.Write((short)2);
        bw.Write((short)16);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dataBytes);
        for (int i = 0; i < sampleCount; i++)
        {
            double v = span[i] * 32767.0;
            short pcm = (short)Math.Clamp(v, -32768.0, 32767.0);
            bw.Write(pcm);
        }
    }}