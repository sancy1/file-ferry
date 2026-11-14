// filepath: src/ImageTextify.Core/Abstractions/IStreamingSpeechSegmenter.cs
// layer:    Contracts
// module:   ImageTextify.Core.Abstractions
// since:    Phase 10 Block 10.B.1
// purpose:  Contract for detecting speech utterances in a live 16 kHz mono
//           float32 audio buffer stream. Complements ISpeechSegmenter, which
//           is bounded (file-path in, region list out). This port is
//           streaming (stream in, utterance-start / utterance-end events
//           out) because a live microphone has no end-of-file.
//
// RELATIONSHIPS
//   Implements : n/a (interface)
//   Depends on : n/a (BCL only)
//   Used by    : ImageTextify.Platform.Audio.StreamingSileroSegmenter (impl),
//                Phase 10.C LiveTranscriptionService (consumer)
//   See also   : ImageTextify.Core.Abstractions.ISpeechSegmenter (bounded variant),
//                ImageTextify.Core.Abstractions.IAudioCaptureService
//
// NOTES
//   - Speech segments are emitted as events because the caller needs to act
//     while audio is arriving. UtteranceStarted tells the orchestrator to
//     begin interim re-transcription; UtteranceEnded delivers the accumulated
//     utterance buffer for final transcription.
//   - Samples are float32 because WhisperVadProcessor.DetectSpeechNoReset*
//     consumes ReadOnlyMemory<float> at Whisper.net 1.9.1 (measured by
//     SileroStreamingApiDiagnostic in this repository). The capture adapter
//     emits 16 kHz mono IEEE float32 as of the Phase 10.A WithFormat fix, so
//     no numeric conversion happens inside the segmenter; only a byte-to-float
//     reinterpret.
//   - Start and End are TimeSpan because SpeechRegion uses TimeSpan and the
//     orchestrator hands these times to IHallucinationSuppressor, whose
//     signature takes IReadOnlyList<SpeechRegion>.
//   - RunAsync completes when the stream ends or cancellation is honored.
//     It does not re-throw OperationCanceledException; cancellation is the
//     normal stop path for a live session.
//   - UtteranceStart or UtteranceEnd may fire zero times (silence for the
//     whole session). The consumer must tolerate that.

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// Detects speech utterances in a live audio buffer stream.
/// </summary>
/// <remarks>
/// <para>
/// The implementation is stateful: it holds a VAD context across the session
/// and emits events when the VAD's view of the stream transitions between
/// silence and speech. The accumulated utterance buffer is delivered in
/// <see cref="UtteranceEnded"/>.
/// </para>
/// <para>
/// Disposal releases the VAD context. A segmenter instance is single-use;
/// create a new one for each live session.
/// </para>
/// </remarks>
public interface IStreamingSpeechSegmenter : IAsyncDisposable
{
    /// <summary>
    /// Raised when speech begins after silence. Fires once per utterance.
    /// </summary>
    event EventHandler<UtteranceStartedEventArgs> UtteranceStarted;

    /// <summary>
    /// Raised when an utterance ends. Carries the accumulated utterance
    /// samples as 16 kHz mono float32. Fires once per utterance.
    /// </summary>
    event EventHandler<UtteranceEndedEventArgs> UtteranceEnded;

    /// <summary>
    /// Runs the segmenter against the supplied audio stream until the stream
    /// ends or cancellation is honored. Emits UtteranceStarted and
    /// UtteranceEnded events on the caller's thread (the thread awaiting this
    /// task) as the VAD observes transitions.
    /// </summary>
    /// <param name="audioStream">
    /// The live audio stream. Yields 16 kHz mono IEEE float32 bytes. Read
    /// until it returns zero or cancellation fires. Not null.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the loop. Honoring this token is the normal stop path; the
    /// implementation does not throw OperationCanceledException on cancel.
    /// </param>
    /// <returns>
    /// A task that completes when the stream ends or cancellation fires.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="audioStream"/> is null.
    /// </exception>
    Task RunAsync(Stream audioStream, CancellationToken cancellationToken);
}

/// <summary>
/// Event arguments for <see cref="IStreamingSpeechSegmenter.UtteranceStarted"/>.
/// </summary>
public sealed class UtteranceStartedEventArgs : EventArgs
{
    /// <summary>
    /// Creates the event args with the utterance start time.
    /// </summary>
    /// <param name="start">
    /// The offset from the start of the session at which speech began.
    /// </param>
    public UtteranceStartedEventArgs(TimeSpan start)
    {
        Start = start;
    }

    /// <summary>
    /// The offset from the start of the session at which speech began.
    /// </summary>
    public TimeSpan Start { get; }
}

/// <summary>
/// Event arguments for <see cref="IStreamingSpeechSegmenter.UtteranceEnded"/>.
/// </summary>
public sealed class UtteranceEndedEventArgs : EventArgs
{
    /// <summary>
    /// Creates the event args with the accumulated utterance samples and
    /// their time bounds.
    /// </summary>
    /// <param name="samples">
    /// The accumulated utterance samples as 16 kHz mono float32. Never null;
    /// may be empty only if the VAD emitted an end without any start (a
    /// state the implementation should not produce). The buffer is owned by
    /// the caller after this event returns.
    /// </param>
    /// <param name="start">
    /// The offset from the start of the session at which speech began.
    /// </param>
    /// <param name="end">
    /// The offset from the start of the session at which speech ended.
    /// Greater than or equal to <paramref name="start"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="samples"/> is the default value.
    /// </exception>
    public UtteranceEndedEventArgs(ReadOnlyMemory<float> samples, TimeSpan start, TimeSpan end)
    {
        Samples = samples;
        Start = start;
        End = end;
    }

    /// <summary>
    /// The accumulated utterance samples as 16 kHz mono float32.
    /// </summary>
    public ReadOnlyMemory<float> Samples { get; }

    /// <summary>
    /// The offset from the start of the session at which speech began.
    /// </summary>
    public TimeSpan Start { get; }

    /// <summary>
    /// The offset from the start of the session at which speech ended.
    /// </summary>
    public TimeSpan End { get; }
}