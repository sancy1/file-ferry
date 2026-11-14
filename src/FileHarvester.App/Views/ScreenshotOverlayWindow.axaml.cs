// filepath: src/FileHarvester.App/Views/ScreenshotOverlayWindow.axaml.cs
// layer:    App View code-behind
// module:   FileHarvester.App.Views
// since:    Phase 14 (Screenshot)
// purpose:  Code-behind for ScreenshotOverlayWindow. Captures the screen before
//           the overlay is visible, shows the frozen bitmap as its background,
//           tracks the drag rectangle, crops on release, and raises either
//           Completed (with a PNG stream + dimensions) or Cancelled.
// RELATIONSHIPS
//   Depends on : Avalonia Window, Canvas, Border, ImageBrush, Bitmap;
//                System.Drawing.Bitmap, Graphics.CopyFromScreen, ImageFormat.Png;
//                ImageTextify.Core.Abstractions.IScreenCaptureService
//   Used by    : FileHarvester.App.ViewModels.ScreenshotViewModel
//   See also   : Phase 14 design plan; the Phase 14 spike
// NOTES
//   - Captures the frozen bitmap in Opened, before this window paints.
//   - Captures in PHYSICAL pixels (Bounds x RenderScaling) so the PNG is exact
//     at any DPI.
//   - Pointer events are on the Canvas, not the Window (the spike's first
//     attempt failed for exactly this reason).
//   - The window does not save to disk; it raises Completed with the crop
//     stream, and the ViewModel writes to Desktop\Screenshot\.

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ImageTextify.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace FileHarvester.App.Views;

/// <summary>
/// Fullscreen dimmed overlay. Captures, dims, drags, crops, then raises
/// Completed or Cancelled.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class ScreenshotOverlayWindow : Window, IDisposable
{
    private readonly IScreenCaptureService _capture;
    private Canvas _canvas = null!;
    private Border _selection = null!;
    private System.Drawing.Bitmap? _frozen;
    private Avalonia.Point _start;
    private bool _dragging;

    /// <summary>Raised with the cropped PNG stream and exact dimensions, on mouse-up.</summary>
    public event Action<Stream, int, int>? Completed;

    /// <summary>Raised when the user presses Escape.</summary>
    public event Action? Cancelled;

    /// <summary>
    /// Parameterless constructor required by the Avalonia XAML loader
    /// (AVLN3001). Resolves the screen capture service from App.Services.
    /// The DI ctor below is what the ViewModel uses.
    /// </summary>
    public ScreenshotOverlayWindow()
        : this(App.Services.GetRequiredService<ImageTextify.Core.Abstractions.IScreenCaptureService>())
    {
    }

    /// <summary>Creates the overlay.</summary>
    /// <param name="capture">The screen capture service. Not null.</param>
    /// <param name="frozen">
    /// Optional pre-captured full-screen bitmap. When supplied (the normal
    /// flow: the ViewModel captures after minimizing, before Show), OnOpened
    /// skips capture entirely and only paints the frozen frame.
    /// </param>
    public ScreenshotOverlayWindow(IScreenCaptureService capture, System.Drawing.Bitmap? frozen = null)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _frozen = frozen;
        InitializeComponent();
        _canvas = this.FindControl<Canvas>("RootCanvas")!;
        _selection = this.FindControl<Border>("SelectionBorder")!;
        _canvas.PointerPressed += OnPointerPressed;
        _canvas.PointerMoved += OnPointerMoved;
        _canvas.PointerReleased += OnPointerReleased;
        KeyDown += OnKeyDown;
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        try
        {
            if (_frozen is null)
            {
                var scale = RenderScaling;
                var pw = (int)Math.Round(Bounds.Width * scale);
                var ph = (int)Math.Round(Bounds.Height * scale);
                if (pw <= 0 || ph <= 0) return;

                var captured = _capture.CaptureFullScreenAsync(CancellationToken.None).GetAwaiter().GetResult();
                _frozen = new System.Drawing.Bitmap(captured.FrameStream);
                captured.FrameStream.Dispose();
            }

            var ms = new MemoryStream();
            _frozen.Save(ms, ImageFormat.Png);
            ms.Position = 0;
            var avaloniaBmp = new Avalonia.Media.Imaging.Bitmap(ms);
            _canvas.Background = new ImageBrush(avaloniaBmp) { Stretch = Stretch.Fill };
        }
        catch
        {
            Cancelled?.Invoke();
            Close();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancelled?.Invoke();
            Close();
        }
    }
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragging = true;
        _start = e.GetPosition(_canvas);
        Canvas.SetLeft(_selection, _start.X);
        Canvas.SetTop(_selection, _start.Y);
        _selection.Width = 0;
        _selection.Height = 0;
        _selection.IsVisible = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(_canvas);
        var x = Math.Min(_start.X, p.X);
        var y = Math.Min(_start.Y, p.Y);
        var w = Math.Abs(p.X - _start.X);
        var h = Math.Abs(p.Y - _start.Y);
        Canvas.SetLeft(_selection, x);
        Canvas.SetTop(_selection, y);
        _selection.Width = w;
        _selection.Height = h;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;

        var cw = _canvas.Bounds.Width;
        var ch = _canvas.Bounds.Height;
        if (_frozen is null || cw <= 0 || ch <= 0)
        {
            Cancelled?.Invoke();
            Close();
            return;
        }

        // Map canvas DIPs to image pixels by ratio, so DPI scaling and any
        // difference between window size and image size cannot skew the crop.
        var sx = _frozen.Width / cw;
        var sy = _frozen.Height / ch;

        var x = (int)Math.Round(Canvas.GetLeft(_selection) * sx);
        var y = (int)Math.Round(Canvas.GetTop(_selection) * sy);
        var w = (int)Math.Round(_selection.Width * sx);
        var h = (int)Math.Round(_selection.Height * sy);

        x = Math.Clamp(x, 0, _frozen.Width - 1);
        y = Math.Clamp(y, 0, _frozen.Height - 1);
        w = Math.Min(w, _frozen.Width - x);
        h = Math.Min(h, _frozen.Height - y);

        if (w <= 0 || h <= 0)
        {
            Cancelled?.Invoke();
            Close();
            return;
        }
        var crop = new System.Drawing.Bitmap(w, h);
        using (var g = Graphics.FromImage(crop))
        {
            g.DrawImage(_frozen, new Rectangle(0, 0, w, h), new Rectangle(x, y, w, h), GraphicsUnit.Pixel);
        }
        var ms = new MemoryStream();
        crop.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var outW = crop.Width;
        var outH = crop.Height;
        crop.Dispose();
        Completed?.Invoke(ms, outW, outH);
        Close();
    }

    /// <summary>
    /// Disposes the frozen bitmap and detaches the pointer handlers.
    /// The window is closed by the overlay flow; this releases the GDI
    /// surface so repeated screenshot invocations do not leak.
    /// </summary>
    public void Dispose()
    {
        try { _frozen?.Dispose(); } catch { }
        _frozen = null;
        GC.SuppressFinalize(this);
    }
}
