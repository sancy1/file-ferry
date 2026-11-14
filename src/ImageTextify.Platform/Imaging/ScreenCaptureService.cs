// ============================================================================
// filepath: src/ImageTextify.Platform/Imaging/ScreenCaptureService.cs
// layer:    Platform / Infrastructure
// module:   ImageTextify.Platform.Imaging
// since:    Phase 14 (Screenshot)
// purpose:  Implements IScreenCaptureService over System.Drawing.Graphics
//           CopyFromScreen. Produces a PNG stream of the exact pixel rectangle
//           requested — never reduced, never scaled.
//
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.IScreenCaptureService
//   Depends on : System.Drawing (Bitmap, Graphics, CopyFromScreen, ImageFormat)
//   Used by    : ImageTextifyServiceCollectionExtensions (DI, Windows-guarded);
//                FileHarvester.App.ViewModels.ScreenshotViewModel;
//                FileHarvester.App.Views.ScreenshotOverlayWindow
//   See also   : IScreenCaptureService.cs (the contract);
//                Phase 14 design plan;
//                the Phase 14 spike (PASS requested=400x300 actual=400x300)
//
// NOTES
//   - Windows-only: the class carries [SupportedOSPlatform("windows")] so the
//     analyzer treats every System.Drawing call site as guarded. The DI
//     registration is what gates availability.
//   - No scaling, no cropping beyond the requested rectangle, no format change:
//     the produced PNG is exactly width x height pixels.
//   - The result is a MemoryStream in memory; the caller owns it. No file is
//     written. No disk I/O. No temp files.
//   - CopyFromScreen is synchronous and cannot be interrupted mid-copy. The
//     cancellation token is honoured at the boundaries (before the copy and
//     before returning the stream).
//   - Multi-monitor and non-100% DPI layouts are not explicitly handled in
//     Phase 1; the caller passes screen-pixel coordinates and the platform
//     returns screen-pixel dimensions. If the caller's coordinates and the
//     platform's pixels disagree, the caller's error, not the service's.
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// IScreenCaptureService over System.Drawing.Graphics.CopyFromScreen.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ScreenCaptureService : IScreenCaptureService
{
    /// <inheritdoc/>
    public Task<CapturedScreenshot> CaptureRegionAsync(
        int x, int y, int width, int height, CancellationToken cancellationToken)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(x, y, 0, 0, new Size(width, height));
            }
            cancellationToken.ThrowIfCancellationRequested();
            var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            ms.Position = 0;
            return Task.FromResult(new CapturedScreenshot(ms, bmp.Width, bmp.Height, DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new ScreenCaptureUnavailableException(
                "Screen region capture failed: " + ex.Message, ex);
        }
    }

    /// <inheritdoc/>
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    /// <inheritdoc/>
    public Task<CapturedScreenshot> CaptureFullScreenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var w = GetSystemMetrics(SM_CXSCREEN);
        var h = GetSystemMetrics(SM_CYSCREEN);
        if (w <= 0 || h <= 0)
        {
            throw new ScreenCaptureUnavailableException("Primary screen bounds unavailable.");
        }
        return CaptureRegionAsync(0, 0, w, h, cancellationToken);
    }
}
