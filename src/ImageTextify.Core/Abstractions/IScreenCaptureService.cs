// ============================================================================
// filepath: src/ImageTextify.Core/Abstractions/IScreenCaptureService.cs
// layer:    Contracts
// module:   ImageTextify.Core.Abstractions
// since:    Phase 14 (Screenshot)
// purpose:  Contract for capturing a rectangle of the screen to a PNG stream.
//           The implementation uses the platform's screen-copy API; the
//           contract carries only BCL types, so the App layer is
//           platform-agnostic.
//
// RELATIONSHIPS
//   Implements : n/a (interface)
//   Depends on : n/a (BCL only)
//   Used by    : ImageTextify.Platform.Imaging.ScreenCaptureService (the
//                Windows implementation using Graphics.CopyFromScreen),
//                FileHarvester.App.ViewModels.ScreenshotViewModel,
//                FileHarvester.App.Views.ScreenshotOverlayWindow (displaying
//                the frozen bitmap for the user to select on)
//   See also   : Phase 14 design plan;
//                CapturedFrame (camera) — same Stream + dimension shape
//
// NOTES
//   - The captured image is NEVER reduced. The record's Width and Height are
//     exactly the pixel dimensions returned by the platform copy. No scaling is
//     applied anywhere in the pipeline (Phase 14 plan section 1).
//   - The caller owns the FrameStream and is responsible for disposing it. The
//     service does not retain it.
//   - Cancellation is honored at the platform boundary. On cancellation, no
//     partial stream is returned.
//   - Windows-only in Phase 1. The overlay pattern is a Windows-native pattern
//     (Phase 14 plan section 2). A macOS or Linux adapter would implement the
//     same interface against a different platform API; the contract does not
//     change.
//   - The interface is implementation-agnostic on purpose: the spike proved
//     Graphics.CopyFromScreen preserves exact pixel dimensions; the future
//     macOS/Linux adapter can be added without touching this file.
// ============================================================================

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// Captures a rectangle of the screen to a PNG stream.
/// </summary>
public interface IScreenCaptureService
{
    /// <summary>
    /// Captures the specified screen rectangle.
    /// </summary>
    /// <param name="x">The left edge of the region, in screen pixels. May be negative for multi-monitor layouts.</param>
    /// <param name="y">The top edge of the region, in screen pixels.</param>
    /// <param name="width">The region width in pixels. Positive.</param>
    /// <param name="height">The region height in pixels. Positive.</param>
    /// <param name="cancellationToken">Cancels the capture. On cancellation no stream is returned.</param>
    /// <returns>
    /// The captured region as a PNG stream with the exact pixel dimensions. The
    /// caller owns the stream.
    /// </returns>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// Thrown when <paramref name="width"/> or <paramref name="height"/> is not positive.
    /// </exception>
    /// <exception cref="ScreenCaptureUnavailableException">
    /// Thrown when the platform capture fails.
    /// </exception>
    Task<CapturedScreenshot> CaptureRegionAsync(
        int x, int y, int width, int height, CancellationToken cancellationToken);

    /// <summary>
    /// Captures the entire primary screen.
    /// </summary>
    /// <param name="cancellationToken">Cancels the capture.</param>
    /// <returns>
    /// The full-screen capture as a PNG stream with the primary screen's exact
    /// pixel dimensions. The caller owns the stream.
    /// </returns>
    /// <exception cref="ScreenCaptureUnavailableException">
    /// Thrown when the platform capture fails.
    /// </exception>
    Task<CapturedScreenshot> CaptureFullScreenAsync(CancellationToken cancellationToken);
}

/// <summary>
/// One captured screenshot. PNG bytes + exact pixel dimensions.
/// </summary>
/// <param name="FrameStream">
/// The PNG bytes. The caller owns the stream and is responsible for disposing
/// it. Not null.
/// </param>
/// <param name="Width">The captured width in pixels. Exactly the pixel count from the platform copy; never scaled.</param>
/// <param name="Height">The captured height in pixels. Exactly the pixel count from the platform copy; never scaled.</param>
/// <param name="CapturedAtUtc">
/// The UTC time the capture completed. Used for correlation and for the
/// suggested filename in the UI. Never used for identification.
/// </param>
public sealed record CapturedScreenshot(
    Stream FrameStream,
    int Width,
    int Height,
    System.DateTimeOffset CapturedAtUtc);

/// <summary>
/// Thrown when screen capture is unavailable on the current platform or when
/// the platform copy fails.
/// </summary>
public sealed class ScreenCaptureUnavailableException : System.Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public ScreenCaptureUnavailableException() : base("Screen capture is unavailable.") { }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The exception message.</param>
    public ScreenCaptureUnavailableException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and inner exception.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ScreenCaptureUnavailableException(string message, System.Exception innerException) : base(message, innerException) { }
}