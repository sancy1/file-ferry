// ============================================================================
// filepath: src/FileHarvester.App/ViewModels/ScreenshotViewModel.cs
// layer:    App / ViewModels
// module:   FileHarvester.App.ViewModels
// since:    Phase 14 (Screenshot)
// purpose:  Sub-ViewModel for the screenshot flow. Opens the overlay window,
//           receives the cropped PNG, writes it to Desktop\Screenshot with a
//           timestamped filename. Holds the state (idle / capturing / saved /
//           cancelled) and the menu command.
//
// RELATIONSHIPS
//   Implements : FileHarvester.App.ViewModels.ViewModelBase, IDisposable
//   Depends on : ImageTextify.Core.Abstractions.IScreenCaptureService,
//                FileHarvester.App.Views.ScreenshotOverlayWindow,
//                System.IO, System.Environment
//   Used by    : MainWindowViewModel; MainWindow.axaml (Screenshot button)
//   See also   : ScreenshotOverlayWindow.axaml.cs (the overlay this opens);
//                Phase 14 design plan (section 3, Piece 3)
//
// NOTES
//   - The capture happens in the overlay window, not here. This VM owns
//     the file save: Desktop\Screenshot\screenshot-{yyyyMMdd-HHmmss}.png.
//   - The overlay window is disposed after it closes so its frozen GDI bitmap
//     is released (the window implements IDisposable; CA1001 and CA1816 drove
//     that in the window's own file).
//   - TakeScreenshotCommand is synchronous: it opens the overlay and returns.
//     The stream arrives asynchronously via the Completed event. The caller
//     (the UI) returns to its idle state until the overlay closes.
//   - File name collision within the same second is possible but rare; the
//     overlay is a user action and cannot fire twice in the same second
//     from the same user. A collision-safe name would need a counter; out of
//     scope for Phase 1.
// ============================================================================

using System;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ImageTextify.Core.Abstractions;
using FileHarvester.App.Views;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// Screenshot sub-ViewModel. Opens the overlay; saves the cropped PNG to
/// Desktop\Screenshot with a timestamped name.
/// </summary>
public sealed class ScreenshotViewModel : ViewModelBase, IDisposable
{
    private readonly IScreenCaptureService _capture;
    private ScreenshotOverlayWindow? _window;
    private string? _lastSavedPath;
    private bool _isCapturing;
    private string _statusText = "Click Screenshot to capture.";
    private bool _disposed;

    /// <summary>Creates the sub-ViewModel.</summary>
    /// <param name="capture">The screen capture service. Not null.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="capture"/> is null.</exception>
    public ScreenshotViewModel(IScreenCaptureService capture)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        TakeScreenshotCommand = new RelayCommand(TakeScreenshot);
    }

    /// <summary>True while the overlay is open.</summary>
    public bool IsCapturing
    {
        get => _isCapturing;
        private set
        {
            if (SetProperty(ref _isCapturing, value))
            {
                OnPropertyChanged(nameof(CanCapture));
            }
        }
    }

    /// <summary>True when the Screenshot button should be enabled.</summary>
    public bool CanCapture => !IsCapturing;

    /// <summary>The path of the last saved screenshot, or null.</summary>
    public string? LastSavedPath
    {
        get => _lastSavedPath;
        private set
        {
            if (SetProperty(ref _lastSavedPath, value))
            {
                OnPropertyChanged(nameof(HasResult));
            }
        }
    }

    /// <summary>True after a successful save.</summary>
    public bool HasResult => !string.IsNullOrEmpty(LastSavedPath);

    /// <summary>The user-facing status line.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Opens the screenshot overlay.</summary>
    public ICommand TakeScreenshotCommand { get; }

    /// <summary>Disposes the overlay window if still open.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (OperatingSystem.IsWindows()) { try { _window?.Dispose(); } catch { } }
        _window = null;
        GC.SuppressFinalize(this);
    }

    private void TakeScreenshot()
    {
        if (IsCapturing) return;
        IsCapturing = true;
        if (!OperatingSystem.IsWindows())
        {
            StatusText = "Screenshot is Windows-only in Phase 14.";
            IsCapturing = false;
            return;
        }
        StatusText = "Select a region...";
        var cloaked = false;
        IntPtr mainHwnd = IntPtr.Zero;
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var folder = Path.Combine(desktop, "Screenshot");
            Directory.CreateDirectory(folder);

            mainHwnd = GetMainWindowHandle();
            if (mainHwnd != IntPtr.Zero) { SetWindowCloaked(mainHwnd, true); cloaked = true; }

            // Cloaking removes the window from DWM composition instantly —
            // no minimize animation, nothing to tear. Flush so the
            // compositor presents the desktop without the app window.
            FlushDwmFrames();

            System.Drawing.Bitmap frozen;
            try
            {
                frozen = CaptureFrozenFrame();
            }
            catch (Exception ex)
            {
                if (mainHwnd != IntPtr.Zero) { SetWindowCloaked(mainHwnd, false); }
                cloaked = false;
                StatusText = "Capture failed: " + ex.Message;
                IsCapturing = false;
                return;
            }

            var window = new Views.ScreenshotOverlayWindow(_capture, frozen);
            _window = window;
            window.Completed += (stream, w, h) =>
            {
                try
                {
                    var name = "screenshot-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".png";
                    var path = Path.Combine(folder, name);
                    using (var fs = File.Create(path)) { stream.CopyTo(fs); }
                    stream.Dispose();
                    LastSavedPath = path;
                    StatusText = "Saved " + w + "x" + h + " to " + path;
                }
                catch (Exception ex)
                {
                    StatusText = "Save failed: " + ex.Message;
                }
                finally
                {
                    IsCapturing = false;
                }
            };
            window.Cancelled += () =>
            {
                StatusText = "Cancelled.";
                IsCapturing = false;
            };
            window.Closed += (_, _) =>
            {
                if (OperatingSystem.IsWindows()) { try { window.Dispose(); } catch { } }
                _window = null;
                IsCapturing = false;
                if (mainHwnd != IntPtr.Zero && OperatingSystem.IsWindows()) { SetWindowCloaked(mainHwnd, false); }
            };
            window.Show();
        }
        catch (Exception ex)
        {
            if (cloaked && mainHwnd != IntPtr.Zero && OperatingSystem.IsWindows()) { SetWindowCloaked(mainHwnd, false); }
            StatusText = "Screenshot failed: " + ex.Message;
            IsCapturing = false;
        }
    }

    [SupportedOSPlatform("windows")]
    private System.Drawing.Bitmap CaptureFrozenFrame()
    {
        // GDI screen reads can tear while DWM is still presenting the
        // post-cloak desktop — a single CopyFromScreen is not trustworthy
        // (the "duplicated desktop" bug). Capture until two consecutive
        // frames are pixel-identical; identical PNG bytes prove the
        // desktop has finished repainting.
        byte[]? previous = null;
        byte[]? stable = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var captured = _capture.CaptureFullScreenAsync(CancellationToken.None).GetAwaiter().GetResult();
            byte[] bytes;
            if (captured.FrameStream is MemoryStream ms)
            {
                bytes = ms.ToArray();
            }
            else
            {
                using var copy = new MemoryStream();
                captured.FrameStream.CopyTo(copy);
                bytes = copy.ToArray();
            }
            captured.FrameStream.Dispose();
            if (previous is not null && previous.AsSpan().SequenceEqual(bytes))
            {
                stable = bytes;
                break;
            }
            previous = bytes;
            Thread.Sleep(50);
        }
        stable ??= previous ?? throw new InvalidOperationException("Screen capture returned no frames.");
        using var stream = new MemoryStream(stable);
        return new System.Drawing.Bitmap(stream);
    }

    // DWMWA_CLOAK removes the window from DWM composition instantly — no
    // minimize animation, so CopyFromScreen cannot catch a half-animated
    // frame (the "duplicated desktop" tearing bug). The window stays
    // cloaked while the overlay is open and un-cloaks when it closes.
    private const int DwmwaCloak = 13;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [SupportedOSPlatform("windows")]
    private static IntPtr GetMainWindowHandle()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is not null)
        {
            return desktop.MainWindow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        }
        return IntPtr.Zero;
    }

    [SupportedOSPlatform("windows")]
    private static void SetWindowCloaked(IntPtr hwnd, bool cloaked)
    {
        if (hwnd == IntPtr.Zero) { return; }
        var value = cloaked ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DwmwaCloak, ref value, sizeof(int));
    }

    [SupportedOSPlatform("windows")]
    private static void FlushDwmFrames()
    {
        for (var i = 0; i < 3; i++) { _ = DwmFlush(); }
    }
}
