// ============================================================================
// FILE:     src/ImageTextify.Core/Abstractions/ICameraCaptureService.cs
// PURPOSE:  Contract for enumerating cameras and capturing a still
//           frame that is then sent into the OCR pipeline. Contract
//           only; implementation and UI are the separate, approval-
//           gated Live Camera Capture phase.
// RELATION: Consumed by the App layer in the Live Camera Capture
//           phase. No implementation in Phase 1.3.
// ============================================================================

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// Enumerates cameras and opens a capture session.
/// </summary>
/// <remarks>
/// <para>
/// A captured frame is a transient buffer. The caller owns the frame
/// stream and is responsible for discarding it. No frame is persisted
/// unless the caller explicitly writes it.
/// </para>
/// <para>
/// Camera access requires an explicit user action. The service never
/// opens the camera on its own.
/// </para>
/// </remarks>
public interface ICameraCaptureService
{
    /// <summary>
    /// Enumerates the cameras available on this machine.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancels the enumeration. The service honors the token.
    /// </param>
    /// <returns>
    /// The available cameras. Empty when no camera is available; the
    /// service does not throw for a missing device.
    /// </returns>
    Task<IReadOnlyList<CameraDevice>> EnumerateAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Opens a capture session on the given camera.
    /// </summary>
    /// <param name="device">The camera to open. Not null.</param>
    /// <param name="options">The session options. Not null.</param>
    /// <param name="cancellationToken">
    /// Cancels the open. The service honors the token.
    /// </param>
    /// <returns>
    /// An open session. The caller owns the session and disposes it.
    /// </returns>
    /// <exception cref="CameraUnavailableException">
    /// Thrown when the camera is denied, disconnected, or otherwise
    /// unavailable. The exception carries a machine-readable reason.
    /// </exception>
    Task<ICameraCaptureSession> StartAsync(
        CameraDevice device,
        CameraCaptureOptions options,
        CancellationToken cancellationToken);
}

/// <summary>
/// An open camera capture session.
/// </summary>
public interface ICameraCaptureSession : IAsyncDisposable
{
    /// <summary>
    /// True while the session is active. False after stop or a
    /// terminal failure.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Captures one still frame.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancels the capture. The session honors the token and leaves no
    /// partial frame behind.
    /// </param>
    /// <returns>
    /// A fresh frame. The caller owns the frame stream.
    /// </returns>
    Task<CapturedFrame> CaptureFrameAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops capturing and releases the camera. Idempotent.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancels the stop. The session honors the token.
    /// </param>
    Task StopAsync(CancellationToken cancellationToken);
}

/// <summary>
/// One enumerated camera.
/// </summary>
/// <param name="DeviceId">
/// The stable identifier for the device. Used to open a session.
/// </param>
/// <param name="DisplayName">
/// A human-readable name safe to display. May contain manufacturer
/// and model information.
/// </param>
/// <param name="IsAvailable">
/// True when the device is currently available for capture.
/// </param>
public sealed record CameraDevice(
    string DeviceId,
    string DisplayName,
    bool IsAvailable);

/// <summary>
/// Options controlling a camera capture session.
/// </summary>
/// <param name="PreferredWidth">
/// The preferred frame width in pixels, or null for the device
/// default.
/// </param>
/// <param name="PreferredHeight">
/// The preferred frame height in pixels, or null for the device
/// default.
/// </param>
/// <param name="IncludeVideoPreview">
/// When true, the session provides a live preview stream in addition
/// to still capture. Default true.
/// </param>
public sealed record CameraCaptureOptions(
    int? PreferredWidth = null,
    int? PreferredHeight = null,
    bool IncludeVideoPreview = true);

/// <summary>
/// One captured still frame.
/// </summary>
/// <param name="FrameStream">
/// The frame bytes. The caller owns the stream and is responsible for
/// discarding it.
/// </param>
/// <param name="Width">The frame width in pixels.</param>
/// <param name="Height">The frame height in pixels.</param>
/// <param name="CapturedAtUtc">
/// The UTC time the frame was captured. Used for correlation, never
/// for identification.
/// </param>
public sealed record CapturedFrame(
    Stream FrameStream,
    int Width,
    int Height,
    DateTimeOffset CapturedAtUtc);

/// <summary>
/// Thrown when camera capture is unavailable.
/// </summary>
public sealed class CameraUnavailableException : Exception
{
    /// <summary>
    /// Creates the exception with a default message.
    /// </summary>
    public CameraUnavailableException()
        : base("The camera is unavailable.")
    {
    }

    /// <summary>
    /// Creates the exception with a message.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public CameraUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates the exception with a message and an inner exception.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public CameraUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}