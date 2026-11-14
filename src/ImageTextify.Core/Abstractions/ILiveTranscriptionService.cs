// ============================================================================
// FILE:     src/ImageTextify.Core/Abstractions/ILiveTranscriptionService.cs
// PURPOSE:  Contract for real-time microphone transcription. Interim
//           text while the speaker talks; finalized text per utterance.
//           No speaker identity is produced in the first release.
// RELATION: Implemented in the Live Conversation Transcription phase.
//           Consumes IAudioCaptureService and IAudioEnhancer.
// ============================================================================

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// Starts a live transcription session over a microphone.
/// </summary>
/// <remarks>
/// <para>
/// Live transcription never starts silently. The caller must have
/// confirmed the user's intent, and the session must present a
/// persistent visible indicator while the microphone is active.
/// </para>
/// <para>
/// Transcription is the default output. Audio and video recording are
/// separate concerns and require a separate explicit user action.
/// </para>
/// </remarks>
public interface ILiveTranscriptionService
{
    /// <summary>
    /// Opens a live transcription session.
    /// </summary>
    /// <param name="options">The session options. Not null.</param>
    /// <param name="cancellationToken">
    /// Cancels the open. The service honors the token.
    /// </param>
    /// <returns>
    /// An open session. The caller owns the session and disposes it.
    /// </returns>
    /// <exception cref="AudioUnavailableException">
    /// Thrown when the microphone is denied, disconnected, or
    /// otherwise unavailable.
    /// </exception>
    Task<ILiveTranscriptionSession> StartAsync(
        LiveTranscriptionOptions options,
        CancellationToken cancellationToken);
}

/// <summary>
/// An open live transcription session.
/// </summary>
public interface ILiveTranscriptionSession : IAsyncDisposable
{
    /// <summary>
    /// Raised when interim text is available while the speaker is still
    /// talking. Interim text may be revised by a later event.
    /// </summary>
    event EventHandler<InterimTextReceivedEventArgs> InterimTextReceived;

    /// <summary>
    /// Raised when a completed utterance has been finalized. Finalized
    /// text is not revised by later events.
    /// </summary>
    event EventHandler<FinalTextReceivedEventArgs> FinalTextReceived;

    /// <summary>
    /// Raised for each timestamped transcript segment.
    /// </summary>
    event EventHandler<SegmentReceivedEventArgs> SegmentReceived;

    /// <summary>
    /// Raised for a quality signal: noise, clipping, silence, low
    /// confidence, distance, echo, or a microphone problem.
    /// </summary>
    event EventHandler<QualityWarningReceivedEventArgs> QualityWarningReceived;

    /// <summary>
    /// Raised when the session starts or stops transcribing a finalized
    /// utterance. Transcription can take seconds; the UI should show a
    /// progress indication while <see cref="TranscriptionActivityEventArgs.IsTranscribing"/>
    /// is true.
    /// </summary>
    event EventHandler<TranscriptionActivityEventArgs> TranscriptionActivityChanged;

    /// <summary>
    /// The current session state.
    /// </summary>
    LiveTranscriptionState State { get; }

    /// <summary>
    /// Stops listening, finalizes any pending text, and releases the
    /// microphone. Idempotent.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancels the stop. The session honors the token.
    /// </param>
    Task StopAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Options controlling a live transcription session.
/// </summary>
/// <param name="DeviceId">
/// The identifier of the microphone to use, or null for the system
/// default.
/// </param>
/// <param name="Language">
/// The language hint for the recognizer, or null for auto-detection.
/// </param>
/// <param name="EnhancementOptions">
/// The audio enhancement options applied before recognition. Not null.
/// </param>
public sealed record LiveTranscriptionOptions(
    string? DeviceId = null,
    string? Language = null,
    AudioEnhancementOptions? EnhancementOptions = null);

/// <summary>
/// The state of a live transcription session.
/// </summary>
public enum LiveTranscriptionState
{
    /// <summary>The session is open but not listening.</summary>
    Idle = 0,

    /// <summary>The session is listening and producing text.</summary>
    Listening = 1,

    /// <summary>The session is paused. The microphone is held but no text is produced.</summary>
    Paused = 2,

    /// <summary>The session is stopping. Pending text is being finalized.</summary>
    Stopping = 3,

    /// <summary>The session ended with a failure. A new session is required to resume.</summary>
    Failed = 4,
}

/// <summary>
/// Event arguments for <see cref="ILiveTranscriptionSession.InterimTextReceived"/>.
/// </summary>
public sealed class InterimTextReceivedEventArgs : EventArgs
{
    /// <summary>
    /// Creates the event args with the interim text.
    /// </summary>
    /// <param name="text">The interim text. Not null.</param>
    public InterimTextReceivedEventArgs(string text)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    /// <summary>
    /// The interim text. May be revised by a later event.
    /// </summary>
    public string Text { get; }
}

/// <summary>
/// Event arguments for <see cref="ILiveTranscriptionSession.FinalTextReceived"/>.
/// </summary>
public sealed class FinalTextReceivedEventArgs : EventArgs
{
    /// <summary>
    /// Creates the event args with the finalized text.
    /// </summary>
    /// <param name="text">The finalized text. Not null.</param>
    public FinalTextReceivedEventArgs(string text)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    /// <summary>
    /// The finalized text. Not revised by later events.
    /// </summary>
    public string Text { get; }
}

/// <summary>
/// Event arguments for <see cref="ILiveTranscriptionSession.SegmentReceived"/>.
/// </summary>
public sealed class SegmentReceivedEventArgs : EventArgs
{
    /// <summary>
    /// Creates the event args with the segment.
    /// </summary>
    /// <param name="segment">The transcript segment. Not null.</param>
    public SegmentReceivedEventArgs(TranscriptSegment segment)
    {
        Segment = segment ?? throw new ArgumentNullException(nameof(segment));
    }

    /// <summary>
    /// The transcript segment.
    /// </summary>
    public TranscriptSegment Segment { get; }
}

/// <summary>
/// Event arguments for <see cref="ILiveTranscriptionSession.QualityWarningReceived"/>.
/// </summary>
public sealed class QualityWarningReceivedEventArgs : EventArgs
{
    /// <summary>
    /// Creates the event args with the warning.
    /// </summary>
    /// <param name="warning">The warning text. Not null.</param>
    public QualityWarningReceivedEventArgs(string warning)
    {
        Warning = warning ?? throw new ArgumentNullException(nameof(warning));
    }

    /// <summary>
    /// The warning text. Safe to display and log.
    /// </summary>
    public string Warning { get; }
}

/// <summary>
/// Event arguments for <see cref="ILiveTranscriptionSession.TranscriptionActivityChanged"/>.
/// </summary>
public sealed class TranscriptionActivityEventArgs : EventArgs
{
    /// <summary>
    /// Creates the event args.
    /// </summary>
    /// <param name="isTranscribing">
    /// True while an utterance is being transcribed; false when done.
    /// </param>
    public TranscriptionActivityEventArgs(bool isTranscribing)
    {
        IsTranscribing = isTranscribing;
    }

    /// <summary>
    /// True while an utterance is being transcribed; false when done.
    /// </summary>
    public bool IsTranscribing { get; }
}