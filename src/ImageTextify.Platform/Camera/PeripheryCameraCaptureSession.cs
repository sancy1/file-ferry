// ============================================================================
// filepath: src/ImageTextify.Platform/Camera/PeripheryCameraCaptureSession.cs
// layer:    Platform / Infrastructure
// module:   ImageTextify.Platform.Camera
// since:    Phase 9 (Camera Capture)
// purpose:  Implements ICameraCaptureSession over an open Periphery.Camera
//           CameraSession. Captures one still frame (PNG via NV12->BGRA->PNG),
//           stops the capture, and releases the device.
//
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.ICameraCaptureSession
//   Depends on : Periphery.Camera.CameraSession, Periphery.Camera.LeasedCameraFrame,
//                Periphery.Camera.CameraFramePinning, Periphery.Camera.CameraFramePin,
//                Periphery.Camera.CameraPixelFormat, SkiaSharp
//   Used by    : ImageTextifyViewModel (via the interface) in Phase 9 increments.
//   See also   : PeripheryCameraCaptureService.cs (opens this)
//
// NOTES
//   - The PNG encoding lives here because a captured frame is PNG by the Phase 9
//     decision (one default format). When step 10 (Save frame button) lands,
//     the encoder moves to ImageTextify.Platform/Imaging/FrameImageWriter.cs
//     and this class delegates. Until then, keep it here to avoid building ahead
//     of the requirement.
//   - The negotiated pixel format may be BGRA/RGBA/RGB/NV12/etc. Only NV12 is
//     converted; direct formats are copied row by row using the stride. Other
//     planar formats throw NotSupportedException in step 3 and are deferred to
//     step 10; the spike proved NV12 (the actual default on the test device).
//   - DisposeAsync is the only correct teardown. StopCaptureAsync alone does not
//     release the device.
// ============================================================================

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;
using Periphery.Camera;
using SkiaSharp;

namespace ImageTextify.Platform.Camera;

/// <summary>
/// An open camera session. Owns the underlying Periphery session.
/// </summary>
/// <remarks>
/// <para>
/// Not thread-safe. The caller must serialize calls to
/// <see cref="CaptureFrameAsync"/> and <see cref="StopAsync"/>. Dispose
/// performs an idempotent stop and release.
/// </para>
/// </remarks>
internal sealed class PeripheryCameraCaptureSession : ICameraCaptureSession
{
    private readonly CameraSession _session;
    private bool _stopped;
    private bool _disposed;

    /// <summary>
    /// Wraps an already-opened Periphery camera session (capture is already
    /// started). The wrapper owns the session from this point on.
    /// </summary>
    /// <param name="session">The open Periphery session. Not null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session"/> is null.</exception>
    public PeripheryCameraCaptureSession(CameraSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <inheritdoc/>
    public bool IsActive => !_stopped && !_disposed;

    /// <inheritdoc/>
    /// <remarks>
    /// Reads one frame from Periphery, encodes it as PNG (see the class remarks
    /// for the NV12 vs direct copy handling), and returns a fresh
    /// <see cref="MemoryStream"/> the caller owns. Cancellation is honored on
    /// the underlying read and produces no partial frame.
    /// </remarks>
    public async Task<CapturedFrame> CaptureFrameAsync(CancellationToken cancellationToken)
    {
        if (!IsActive) throw new InvalidOperationException("Camera session is not active.");

        var frame = await _session
            .ReadFrameAsync(new Periphery.Camera.CameraCaptureOptions(TimeSpan.FromSeconds(5)), cancellationToken)
            .ConfigureAwait(false);

        if (frame is null)
        {
            throw new CameraUnavailableException("Capture failed: no frame returned.");
        }

        var png = EncodePng(frame);
        return new CapturedFrame(
            FrameStream: new MemoryStream(png, writable: false),
            Width: frame.Width,
            Height: frame.Height,
            CapturedAtUtc: DateTimeOffset.UtcNow);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Idempotent. Stops the underlying capture. The device is released by
    /// <see cref="DisposeAsync"/>, not by Stop.
    /// </remarks>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stopped || _disposed) return;
        _stopped = true;
        try
        {
            await _session.StopCaptureAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // A stop failure must not prevent disposal from releasing the device.
            // The device is released in DisposeAsync regardless.
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try { await StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
        try { await _session.DisposeAsync().ConfigureAwait(false); } catch { }
    }

    /// <summary>
    /// Encodes a <see cref="Periphery.Camera.LeasedCameraFrame"/> to PNG bytes. NV12 frames are
    /// converted YUV->BGRA in-process; direct formats (BGRA/RGBA) are copied
    /// row by row using the frame stride.
    /// </summary>
    /// <param name="frame">The frame. Not null.</param>
    /// <returns>PNG bytes.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when the frame is a planar format other than NV12. Deferred to
    /// step 10 if it ever occurs; the test device negotiates NV12.
    /// </exception>
    private static byte[] EncodePng(Periphery.Camera.LeasedCameraFrame frame)
    {
        int w = frame.Width, h = frame.Height;
        byte[] bgra = new byte[w * h * 4];

        if (frame.PixelFormat == CameraPixelFormat.Nv12)
        {
            using var yPin = CameraFramePinning.PinPlane(frame, 0);
            using var uvPin = CameraFramePinning.PinPlane(frame, 1);
            int yStride = yPin.Stride, uvStride = uvPin.Stride;
            byte[] yPlane = new byte[yPin.Length];
            byte[] uvPlane = new byte[uvPin.Length];
            System.Runtime.InteropServices.Marshal.Copy(yPin.Scan0, yPlane, 0, yPin.Length);
            System.Runtime.InteropServices.Marshal.Copy(uvPin.Scan0, uvPlane, 0, uvPin.Length);

            // WHAT: BT.601 YUV->RGB (video range, Y in 16..235).
            // WHY:  Media Foundation delivers NV12 in this range; full-range
            //       conversion would clip highlights and crush shadows.
            for (int y = 0; y < h; y++)
            {
                int yo = y * yStride;
                int uo = (y / 2) * uvStride;
                for (int x = 0; x < w; x++)
                {
                    int Y = yPlane[yo + x];
                    int U = uvPlane[uo + (x & ~1)] - 128;
                    int V = uvPlane[uo + (x & ~1) + 1] - 128;
                    int C = Y - 16;
                    int R = (298 * C + 409 * V + 128) >> 8;
                    int G = (298 * C - 100 * U - 208 * V + 128) >> 8;
                    int B = (298 * C + 516 * U + 128) >> 8;
                    R = R < 0 ? 0 : (R > 255 ? 255 : R);
                    G = G < 0 ? 0 : (G > 255 ? 255 : G);
                    B = B < 0 ? 0 : (B > 255 ? 255 : B);
                    int bi = (y * w + x) * 4;
                    bgra[bi + 0] = (byte)B;
                    bgra[bi + 1] = (byte)G;
                    bgra[bi + 2] = (byte)R;
                    bgra[bi + 3] = 255;
                }
            }
        }
        else if (frame.PixelFormat == CameraPixelFormat.Bgra32 ||
                 frame.PixelFormat == CameraPixelFormat.Rgba32)
        {
            using var pin = CameraFramePinning.Pin(frame);
            byte[] src = new byte[pin.Length];
            System.Runtime.InteropServices.Marshal.Copy(pin.Scan0, src, 0, pin.Length);
            for (int y = 0; y < h; y++)
                Array.Copy(src, y * pin.Stride, bgra, y * w * 4, w * 4);
            if (frame.PixelFormat == CameraPixelFormat.Rgba32)
            {
                for (int i = 0; i < bgra.Length; i += 4)
                {
                    byte r = bgra[i + 0];
                    bgra[i + 0] = bgra[i + 2];
                    bgra[i + 2] = r;
                }
            }
        }
        else
        {
            throw new NotSupportedException(
                "PNG encode does not support camera pixel format '" + frame.PixelFormat +
                "'. NV12, BGRA32 and RGBA32 are supported; extend in step 10.");
        }

        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(
            bgra, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            using var pixmap = new SKPixmap(info, handle.AddrOfPinnedObject(), w * 4);
            using var image = SKImage.FromPixels(pixmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        finally { handle.Free(); }
    }
}
