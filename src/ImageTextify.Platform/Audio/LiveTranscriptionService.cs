// filepath: src/ImageTextify.Platform/Audio/LiveTranscriptionService.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Audio
// since:    Phase 10 Block 10.C.2
// purpose:  Factory that opens an IAudioCaptureSession and constructs a
//           LiveTranscriptionSession. The factory does NOT implement the
//           interim timer, final transcription, suppressor call, or quality
//           warnings; those live in the session (Block 10.C.3). The factory's
//           job is capture startup and session construction.
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.ILiveTranscriptionService
//   Depends on : IAudioCaptureService, IStreamingSpeechSegmenter,
//                ISpeechRecognizer, IHallucinationSuppressor
//   Used by    : Phase 10.D ViewModel (via DI, to be wired)
//   See also   : ImageTextify.Core.Abstractions.ILiveTranscriptionService,
//                ImageTextify.Platform.Audio.LiveTranscriptionSession,
//                ADR-002
// NOTES
//   - The run loop is started fire-and-forget. The session's RunAsync is
//     responsible for catching its own exceptions and transitioning to
//     Failed; the factory does not observe the returned Task.
//   - SampleRateHz is fixed at 16000 and Channels at 1 to match the live
//     segmenter input contract and the NAudioAudioCaptureService format.
//   - The factory passes the LiveTranscriptionOptions.DeviceId directly
//     to AudioCaptureOptions. The "loopback:" prefix convention is
//     handled by NAudioAudioCaptureService, not here.

using System;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;

namespace ImageTextify.Platform.Audio;

/// <summary>
/// Factory that opens an <see cref="IAudioCaptureService"/> session and
/// constructs a <see cref="LiveTranscriptionSession"/>. Registered at
/// composition time (Block 10.D). The session owns the run loop.
/// </summary>
public sealed class LiveTranscriptionService : ILiveTranscriptionService
{
    private readonly IAudioCaptureService _captureService;
    private readonly IStreamingSpeechSegmenter _segmenter;
    private readonly ISpeechRecognizer _recognizer;
    private readonly IHallucinationSuppressor _suppressor;
    private readonly ILiveRecordingService? _recordingService;

    /// <summary>
    /// Constructs the factory.
    /// </summary>
    /// <param name="captureService">The audio capture service. Not null.</param>
    /// <param name="segmenter">The streaming segmenter. Not null. Single-use.</param>
    /// <param name="recognizer">The speech recognizer. Not null.</param>
    /// <param name="suppressor">The hallucination suppressor. Not null.</param>
    /// <param name="recordingService">The optional live recorder. Null means recording is disabled.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public LiveTranscriptionService(
        IAudioCaptureService captureService,
        IStreamingSpeechSegmenter segmenter,
        ISpeechRecognizer recognizer,
        IHallucinationSuppressor suppressor,
        ILiveRecordingService? recordingService = null)
    {
        ArgumentNullException.ThrowIfNull(captureService);
        ArgumentNullException.ThrowIfNull(segmenter);
        ArgumentNullException.ThrowIfNull(recognizer);
        ArgumentNullException.ThrowIfNull(suppressor);
        _captureService = captureService;
        _segmenter = segmenter;
        _recognizer = recognizer;
        _suppressor = suppressor;
        _recordingService = recordingService;
    }

    /// <inheritdoc/>
    /// <exception cref="AudioUnavailableException">
    /// Thrown when the microphone is denied, disconnected, or otherwise
    /// unavailable. The exception carries a machine-readable reason.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is already cancelled.
    /// </exception>
    public async Task<ILiveTranscriptionSession> StartAsync(
        LiveTranscriptionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var captureOptions = new AudioCaptureOptions(
            DeviceId: options.DeviceId,
            SampleRateHz: 16000,
            Channels: 1);

        var capture = await _captureService
            .StartAsync(captureOptions, cancellationToken)
            .ConfigureAwait(false);

        var audioStream = _recordingService is not null
            ? _recordingService.WrapCaptureStream(capture.AudioStream)
            : capture.AudioStream;

        var session = new LiveTranscriptionSession(
            _captureService,
            _segmenter,
            _recognizer,
            _suppressor,
            capture,
            audioStream,
            options.Language,
            LiveTranscriptionState.Listening);

        session.StartRun(cancellationToken);
        return session;
    }
}