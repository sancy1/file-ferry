// ============================================================================
// FILE:     src/ImageTextify.Core/Abstractions/IAudioCaptureService.cs
// PURPOSE:  Contract for microphone capture. Contract only;
//           implementation and UI are the separate, approval-gated
//           Live Conversation Transcription phase.
// RELATION: Consumed by ILiveTranscriptionService implementations and
//           by the App layer in the Live Conversation Transcription
//           phase. No implementation in Phase 1.3.
// ============================================================================

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// Opens a microphone capture session.
/// </summary>
/// <remarks>
/// <para>
/// Microphone access requires an explicit user action. The service
/// never opens the microphone on its own.
/// </para>
/// <para>
/// Captured audio is a transient buffer. The caller owns the session
/// and is responsible for disposing it and clearing any temporary
/// storage when the session ends.
/// </para>
/// </remarks>
public interface IAudioCaptureService
{
    /// <summary>
    /// Opens a capture session on the selected microphone.
    /// </summary>
    /// <param name="options">
    /// Capture options including device selection. Not null.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the open. The service honors the token.
    /// </param>
    /// <returns>
    /// An open session. The caller owns the session and disposes it.
    /// </returns>
    /// <exception cref="AudioUnavailableException">
    /// Thrown when the microphone is denied, disconnected, or
    /// otherwise unavailable. The exception carries a machine-readable
    /// reason.
    /// </exception>
    Task<IAudioCaptureSession> StartAsync(
        AudioCaptureOptions options,
        CancellationToken cancellationToken);
}

/// <summary>
/// An open microphone capture session.
/// </summary>
public interface IAudioCaptureSession : IAsyncDisposable
{
    /// <summary>
    /// True while the session is capturing. False after stop or a
    /// terminal failure.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// The raw audio stream. The caller reads from this stream and is
    /// responsible for clearing any buffered data when done.
    /// </summary>
    Stream AudioStream { get; }

    /// <summary>
    /// Stops capturing and releases the microphone. Idempotent.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancels the stop. The session honors the token.
    /// </param>
    Task StopAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Options controlling microphone capture.
/// </summary>
/// <param name="DeviceId">
/// The identifier of the microphone to open, or null to use the
/// system default.
/// </param>
/// <param name="SampleRateHz">
/// The requested sample rate. Defaults to 16000, the rate the
/// transcription pipeline expects.
/// </param>
/// <param name="Channels">
/// The requested channel count. Defaults to 1, mono.
/// </param>
public sealed record AudioCaptureOptions(
    string? DeviceId = null,
    int SampleRateHz = 16000,
    int Channels = 1);

/// <summary>
/// Thrown when microphone capture is unavailable.
/// </summary>
public sealed class AudioUnavailableException : Exception
{
    /// <summary>
    /// Creates the exception with a default message.
    /// </summary>
    public AudioUnavailableException()
        : base("The microphone is unavailable.")
    {
    }

    /// <summary>
    /// Creates the exception with a message.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public AudioUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates the exception with a message and an inner exception.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public AudioUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}