// ============================================================================
// filepath: src/FileHarvester.App/ViewModels/CameraCaptureViewModel.cs
// layer:    App / ViewModels
// module:   FileHarvester.App.ViewModels
// since:    Phase 9 (Camera Capture)
// purpose:  Camera capture sub-ViewModel for the ImageTextify camera workspace.
//           Owns enumeration, session lifecycle, frozen-frame state, and the
//           commands the CameraCapturePage binds. Exposes the Periphery
//           DeviceInfo for the live preview control.
// RELATIONSHIPS
//   Implements : FileHarvester.App.ViewModels.ViewModelBase, IAsyncDisposable
//   Depends on : ImageTextify.Core.Abstractions.ICameraCaptureService,
//                ImageTextify.Core.Abstractions.CameraDevice,
//                ImageTextify.Core.Abstractions.CameraCaptureOptions,
//                ImageTextify.Core.Abstractions.CapturedFrame,
//                ImageTextify.Core.Abstractions.CameraUnavailableException,
//                Periphery.Devices, Periphery.DeviceInfo (preview binding)
//   Used by    : MainWindowViewModel.Camera; CameraCapturePage.axaml
//   See also   : PeripheryCameraCaptureService.cs (the service this uses);
//                scope document Part 1 and Part 10 decision 6 (modal session)
// NOTES
//   - A1a design: the App layer calls Periphery.Devices directly for the
//     Periphery.DeviceInfo the CameraPreview control requires. The contract
//     CameraDevice record carries only DeviceId/DisplayName/IsAvailable
//     (finding F-001); the preview control needs DeviceInfo. This is
//     acceptable because Periphery.Camera.Avalonia is a direct dependency
//     of the App layer (the control lives there).
//   - Step 9 stubs: UseFrameCommand and SaveFrameCommand dispose the frozen
//     frame and set status. Step 10 wires the PNG save; step 11 wires OCR.
//   - The frozen frame stream is caller-owned; this VM disposes it on
//     Discard, Retake (before re-capture), Close, and DisposeAsync.
// ============================================================================

using System.Globalization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Platform.Storage;
using ImageTextify.Core.Abstractions;
using Periphery;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The camera capture workspace view model.
/// </summary>
/// <remarks>
/// <para>
/// One instance per application. Holds at most one active
/// <see cref="ICameraCaptureSession"/>. Capture freezes a frame; Retake returns
/// to live preview; Discard and Close dispose the frame; Use this frame hands
/// the frame to OCR (step 11); Save frame writes a PNG (step 10).
/// </para>
/// <para>
/// Not thread-safe. All commands are expected to run on the UI thread.
/// </para>
/// </remarks>
public sealed class CameraCaptureViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly ICameraCaptureService _camera;
    private readonly Func<Stream, string, CancellationToken, Task>? _onUseFrame;
    private readonly Func<Stream, string, CancellationToken, Task>? _onSaveFrame;
    private ICameraCaptureSession? _session;
    private CapturedFrame? _frozenFrame;
    private CameraDevice? _selectedDevice;
    private DeviceInfo? _previewDevice;
    private bool _isActive;
    private bool _isFrozen;
    private string _statusText = "No camera open.";
    private string _unavailabilityText = string.Empty;
    private bool _showUnavailability;

    /// <summary>
    /// Creates the sub-ViewModel.
    /// </summary>
    /// <param name="camera">The camera capture service. Not null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="camera"/> is null.</exception>
    public CameraCaptureViewModel(
        ICameraCaptureService camera,
        Func<Stream, string, CancellationToken, Task>? onUseFrame = null,
        Func<Stream, string, CancellationToken, Task>? onSaveFrame = null)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _onUseFrame = onUseFrame;
        _onSaveFrame = onSaveFrame;

        CaptureCommand = new AsyncRelayCommand<object>(_ => CaptureAsync());
        OpenCommand = new AsyncRelayCommand<object>(_ => StartAsync(CancellationToken.None));
        RetakeCommand = new RelayCommand(Retake);
        DiscardCommand = new RelayCommand(() => _ = DiscardAsync());
        UseFrameCommand = new AsyncRelayCommand<object>(_ => UseFrameAsync());
        SaveFrameCommand = new AsyncRelayCommand<object>(_ => SaveFrameAsync());
        CloseCommand = new AsyncRelayCommand<object>(_ => CloseAsync());
    }

    /// <summary>The enumerated cameras from the contract service.</summary>
    public ObservableCollection<CameraDevice> Devices { get; } = new();

    /// <summary>The currently selected device, if any.</summary>
    public CameraDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                UpdatePreviewDevice();
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(DeviceLabel));
                if (!_suppressAutoStart && !IsActive && value is not null)
                {
                    _ = StartAsync(CancellationToken.None);
                }
            }
        }
    }

    private void UpdatePreviewDevice()
    {
        PreviewDevice = _selectedDevice is null
            ? null
            : _previewDevices.FirstOrDefault(d => d.Id.Value == _selectedDevice.DeviceId);
    }

    private IReadOnlyList<DeviceInfo> _previewDevices = Array.Empty<DeviceInfo>();
    private bool _suppressAutoStart;

    /// <summary>
    /// The Periphery device the CameraPreview control binds to. Set from
    /// <see cref="SelectedDevice"/> by matching DeviceId.
    /// </summary>
    public DeviceInfo? PreviewDevice
    {
        get => _previewDevice;
        private set => SetProperty(ref _previewDevice, value);
    }

    /// <summary>True when a device is selected and no session is active.</summary>
    public bool CanStart => SelectedDevice is not null && !IsActive;

    /// <summary>True while a capture session is active.</summary>
    public bool IsActive
    {
        get => _isActive;
        private set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(IsLive));
            }
        }
    }

    /// <summary>True while a frozen frame is being shown instead of the live preview.</summary>
    public bool IsFrozen
    {
        get => _isFrozen;
        private set
        {
            if (SetProperty(ref _isFrozen, value))
            {
                OnPropertyChanged(nameof(IsLive));
                OnPropertyChanged(nameof(IsPreviewVisible));
            }
        }
    }

    /// <summary>True when the control should show a live feed.</summary>
    public bool IsLive => IsActive && !IsFrozen;

    /// <summary>True when the preview surface is visible (live or frozen).</summary>
    public bool IsPreviewVisible => IsActive;

    /// <summary>The device label for the on-screen indicator.</summary>
    public string DeviceLabel => SelectedDevice is null
        ? "Camera: (none)"
        : "Camera: " + SelectedDevice.DisplayName;

    /// <summary>True when the unavailability banner should be shown.</summary>
    public bool ShowUnavailability
    {
        get => _showUnavailability;
        private set => SetProperty(ref _showUnavailability, value);
    }

    /// <summary>The unavailability message (one of the four scope Part 1.7 states).</summary>
    public string UnavailabilityText
    {
        get => _unavailabilityText;
        private set => SetProperty(ref _unavailabilityText, value);
    }

    /// <summary>The status line under the controls.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Opens (or reopens) the camera session on the selected device.</summary>
    public ICommand OpenCommand { get; }
    /// <summary>Freezes the current frame.</summary>
    public ICommand CaptureCommand { get; }
    /// <summary>Returns to live preview.</summary>
    public ICommand RetakeCommand { get; }
    /// <summary>Discards the frozen frame without using it.</summary>
    public ICommand DiscardCommand { get; }
    /// <summary>Hands the frozen frame to OCR (stub in step 9; wired in step 11).</summary>
    public ICommand UseFrameCommand { get; }
    /// <summary>Saves the frozen frame as PNG (stub in step 9; wired in step 10).</summary>
    public ICommand SaveFrameCommand { get; }
    /// <summary>Stops the camera and returns to ImageTextify.</summary>
    public ICommand CloseCommand { get; }

    /// <summary>
    /// Enumerates cameras from the contract service and from Periphery (for
    /// the preview binding). Safe to call while no session is active.
    /// </summary>
    /// <param name="cancellationToken">Cancels enumeration.</param>
    public async Task RefreshDevicesAsync(CancellationToken cancellationToken)
    {
        ShowUnavailability = false;
        UnavailabilityText = string.Empty;
        Devices.Clear();
        IReadOnlyList<CameraDevice> found;
        try
        {
            found = await _camera.EnumerateAsync(cancellationToken).ConfigureAwait(true);
            _previewDevices = await Periphery.Devices.Enumerate()
                .OfCategory(DeviceCategory.Camera)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(true);
        }
        catch (CameraUnavailableException ex)
        {
            ShowUnavailability = true;
            UnavailabilityText = ex.Message;
            StatusText = "Camera enumeration failed.";
            return;
        }

        foreach (var d in found) Devices.Add(d);

        if (Devices.Count == 0)
        {
            ShowUnavailability = true;
            UnavailabilityText = "No camera detected. Connect a camera and try again.";
            StatusText = "No camera detected.";
        }
        else
        {
            _suppressAutoStart = true;
            try
            {
                // Force a fresh selection even when the re-enumerated device is
                // record-equal to the previous one; otherwise SetProperty returns
                // false and PreviewDevice/DeviceLabel never refresh on relaunch.
                _selectedDevice = null;
                OnPropertyChanged(nameof(SelectedDevice));
                SelectedDevice = Devices[0];
            }
            finally { _suppressAutoStart = false; }
            UpdatePreviewDevice();
            OnPropertyChanged(nameof(DeviceLabel));
            StatusText = IsActive
                ? "Camera open. Click Capture to freeze a frame."
                : "Ready. Click Capture to open the selected camera.";
        }
    }

    /// <summary>
    /// Opens the session on the currently selected device.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (SelectedDevice is null || IsActive) return;
        try
        {
            _session = await _camera
                .StartAsync(SelectedDevice, new CameraCaptureOptions(), cancellationToken)
                .ConfigureAwait(true);
            IsActive = true;
            IsFrozen = false;
            ShowUnavailability = false;
            UnavailabilityText = string.Empty;
            StatusText = "Camera open. Click Capture to freeze a frame.";
            OnPropertyChanged(nameof(DeviceLabel));
        }
        catch (CameraUnavailableException ex)
        {
            ShowUnavailability = true;
            UnavailabilityText = ex.Message;
            StatusText = "Camera unavailable.";
        }
    }

    /// <summary>
    /// Freezes the current frame from the active session.
    /// </summary>
    public async Task CaptureAsync()
    {
        if (_session is null && SelectedDevice is not null) await StartAsync(CancellationToken.None).ConfigureAwait(true);
        if (_session is null || !IsActive) return;
        try
        {
            var frame = await _session.CaptureFrameAsync(CancellationToken.None).ConfigureAwait(true);
            _frozenFrame = frame;
            IsFrozen = true;
            StatusText = "Frame frozen. Retake, Discard, or Use this frame.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Capture cancelled.";
        }
        catch (CameraUnavailableException ex)
        {
            ShowUnavailability = true;
            UnavailabilityText = ex.Message;
            StatusText = "Capture failed.";
        }
    }

    /// <summary>Returns to live preview, disposing the frozen frame.</summary>
    public void Retake()
    {
        DisposeFrozen();
        IsFrozen = false;
        StatusText = "Live preview resumed.";
    }

    /// <summary>Discards the frozen frame without using it.</summary>
    public async Task DiscardAsync()
    {
        DisposeFrozen();
        IsFrozen = false;
        await Task.CompletedTask.ConfigureAwait(true);
        StatusText = "Frame discarded. Live preview resumed.";
    }

    // Step 9 stub: step 11 replaces this with a call to ImageTextifyViewModel.ExtractFromFrameAsync.
    // Step 11: hands the frozen frame to the OCR delegate; dispose the frame either way.
    private async Task UseFrameAsync()
    {
        if (_frozenFrame is null)
        {
            StatusText = "No frame to use. Capture a frame first.";
            return;
        }
        var frame = _frozenFrame;
        _frozenFrame = null;
        IsFrozen = false;
        try
        {
            if (_onUseFrame is not null)
            {
                var name = SelectedDevice is null ? "Camera frame" : SelectedDevice.DisplayName;
                await _onUseFrame(frame.FrameStream, name, CancellationToken.None).ConfigureAwait(true);
                StatusText = "Frame sent to OCR.";
            }
            else
            {
                StatusText = "Frame accepted (no OCR delegate configured).";
            }
        }
        catch (Exception ex)
        {
            StatusText = "OCR failed: " + ex.Message;
        }
        finally
        {
            try { frame.FrameStream.Dispose(); } catch { }
        }
    }

    // Step 9 stub: step 10 replaces this with the PNG save using IStorageProvider.
    // Step 12: hands the frozen frame to the save delegate; delegate owns the path picker.
    private async Task SaveFrameAsync()
    {
        if (_frozenFrame is null)
        {
            StatusText = "No frame to save. Capture a frame first.";
            return;
        }
        if (_onSaveFrame is null)
        {
            StatusText = "Save is not configured.";
            return;
        }
        var suggested = "camera-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".png";
        try
        {
            await _onSaveFrame(_frozenFrame.FrameStream, suggested, CancellationToken.None).ConfigureAwait(true);
            StatusText = "Frame saved.";
        }
        catch (Exception ex)
        {
            StatusText = "Save failed: " + ex.Message;
        }
    }

    /// <summary>Stops the camera and releases the device.</summary>
    public async Task CloseAsync()
    {
        DisposeFrozen();
        if (_session is not null)
        {
            try { await _session.StopAsync(CancellationToken.None).ConfigureAwait(true); }
            catch { /* stop failure must not prevent dispose */ }
            try { await _session.DisposeAsync().ConfigureAwait(true); }
            catch { /* best-effort */ }
            _session = null;
        }
        // Release the preview control's device so a relaunch gets a clean
        // DeviceInfo binding; IsVisible alone does not stop its capture.
        PreviewDevice = null;
        // Clear the selection so picking the device again fires the setter
        // (and auto-start); the dropdown still lists the cameras.
        _suppressAutoStart = true;
        try { _selectedDevice = null; OnPropertyChanged(nameof(SelectedDevice)); OnPropertyChanged(nameof(DeviceLabel)); }
        finally { _suppressAutoStart = false; }
        IsActive = false;
        IsFrozen = false;
        StatusText = "Camera closed. Pick a camera and click Open camera.";
    }

    private void DisposeFrozen()
    {
        if (_frozenFrame is null) return;
        try { _frozenFrame.FrameStream.Dispose(); } catch { }
        _frozenFrame = null;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
