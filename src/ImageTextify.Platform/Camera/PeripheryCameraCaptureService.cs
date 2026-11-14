// ============================================================================
// filepath: src/ImageTextify.Platform/Camera/PeripheryCameraCaptureService.cs
// layer:    Platform / Infrastructure
// module:   ImageTextify.Platform.Camera
// since:    Phase 9 (Camera Capture)
// purpose:  Implements ICameraCaptureService over Periphery.Camera 5.0.0
//           (Windows Media Foundation). Enumerates cameras and opens a
//           capture session. No UI, no preview, no frame lifetime here.
//
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.ICameraCaptureService
//   Depends on : Periphery.Devices, Periphery.DeviceInfo, Periphery.DeviceStatus,
//                Periphery.Camera.* (fully qualified; see NOTES for why),
//                Microsoft.Extensions.Logging.Abstractions (NullLogger<T>),
//                Portfolio.Resilience.Abstractions.IResilienceExecutor
//   Used by    : ImageTextifyServiceCollectionExtensions (DI registration);
//                ImageTextifyViewModel (via the interface) in Phase 9 increments.
//   See also   : PeripheryCameraCaptureSession.cs (the session this opens);
//                ImageTextify.Core/Abstractions/ICameraCaptureService.cs (the contract)
//
// NOTES
//   - Periphery.Camera defines types named CameraDevice and CameraCaptureOptions
//     that collide with our contract types in ImageTextify.Core.Abstractions.
//     This file therefore does NOT import Periphery.Camera; every package type
//     is fully qualified so the contract types resolve unambiguously.
//     (This is the same class of collision the spike hit on CameraPreview.)
//   - The service is stateless. StartAsync opens a fresh session and hands it to
//     the caller. The caller owns the session and must dispose it.
//   - EnumerateAsync never throws for a missing camera; it returns an empty list
//     (contract requirement).
//   - macOS is not supported by Periphery.Camera 5.0.0 (finding F-008). This
//     implementation is Windows + Linux (Media Foundation / V4L2). A macOS
//     adapter is a later phase and would implement the same contract.
//   - Every start/open runs through IResilienceExecutor.ExecuteAsync under
//     PolicyNames.CameraCapture (scope Part 7).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;
using ImageTextify.Platform.Composition;
using Microsoft.Extensions.Logging.Abstractions;
using Periphery;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Errors;

namespace ImageTextify.Platform.Camera;

/// <summary>
/// Implements <see cref="ICameraCaptureService"/> over Periphery.Camera 5.0.0.
/// Stateless; opens sessions that the caller owns.
/// </summary>
/// <remarks>
/// <para>
/// The service has no mutable state and is safe for concurrent enumeration.
/// Each call to <see cref="StartAsync"/> creates an independent
/// <see cref="PeripheryCameraCaptureSession"/>.
/// </para>
/// <para>
/// This type is NOT a session manager, NOT a preview renderer, and NOT a frame
/// store. It opens and returns. Lifetime of the returned session belongs to the
/// caller.
/// </para>
/// </remarks>
internal sealed class PeripheryCameraCaptureService : ICameraCaptureService
{
    private readonly IResilienceExecutor _executor;

    /// <summary>
    /// Creates the service. The resilience executor is required so every camera
    /// open runs under the camera-capture policy.
    /// </summary>
    /// <param name="executor">The resilience executor. Not null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="executor"/> is null.</exception>
    public PeripheryCameraCaptureService(IResilienceExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Uses <c>Devices.Enumerate().OfCategory(DeviceCategory.Camera)</c>. A device
    /// is marked available when its <see cref="DeviceStatus"/> is OK and it is
    /// currently active. The distinction between "in use" and "permission denied"
    /// is not expressible in <see cref="CameraDevice.IsAvailable"/> (contract
    /// limitation, finding F-001); it surfaces from <see cref="StartAsync"/> as a
    /// <see cref="CameraUnavailableException"/> with the specific reason.
    /// </remarks>
    public async Task<IReadOnlyList<CameraDevice>> EnumerateAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<DeviceInfo> found;
        try
        {
            found = await Devices.Enumerate()
                .OfCategory(DeviceCategory.Camera)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CameraUnavailableException(
                "Camera enumeration failed: " + ex.Message, ex);
        }

        return found
            .Select(d => new CameraDevice(
                DeviceId: d.Id.Value,
                DisplayName: d.Name ?? string.Empty,
                IsAvailable: d.Status == DeviceStatus.OK && d.IsActive))
            .ToList();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Opens a camera session through Periphery's builder. Any Periphery
    /// <c>Camera*Exception</c> is mapped to a <see cref="CameraUnavailableException"/>
    /// carrying a human-readable reason (see the adapter comment in
    /// <see cref="MapException"/>). The whole open runs under the camera-capture
    /// resilience policy.
    /// </remarks>
    public async Task<ICameraCaptureSession> StartAsync(
        CameraDevice device,
        CameraCaptureOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(options);

        var info = await FindDeviceAsync(device.DeviceId, cancellationToken).ConfigureAwait(false);
        if (info is null)
        {
            throw new CameraUnavailableException(
                "Camera device is no longer present: " + device.DisplayName);
        }

        try
        {
            var session = await _executor.ExecuteAsync(
                ImageTextifyPolicyNames.CameraCapture,
                async ct =>
                {
                    var built = Periphery.Camera.CameraSession.For(info)
                        .MaxResolution(
                            options.PreferredWidth.GetValueOrDefault(1280),
                            options.PreferredHeight.GetValueOrDefault(720))
                        .WithLogger(NullLogger<Periphery.Camera.CameraSession>.Instance);

                    var opened = await built.OpenAsync(ct).ConfigureAwait(false);
                    await opened.StartCaptureAsync(
                        new Periphery.Camera.CameraCaptureOptions(null), ct).ConfigureAwait(false);
                    return opened;
                },
                fallback: null,
                idempotencyKey: null,
                timeBudgetMs: 10_000,
                ct: cancellationToken).ConfigureAwait(false);

            if (session is null)
            {
                throw new CameraUnavailableException("Camera session could not be opened.");
            }

            return new PeripheryCameraCaptureSession(session);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CameraUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw MapException(ex);
        }
    }

    /// <summary>
    /// Finds a device by its stable id. Returns null when the device is gone
    /// between enumeration and start, which the caller turns into a
    /// <see cref="CameraUnavailableException"/>.
    /// </summary>
    private static async Task<DeviceInfo?> FindDeviceAsync(string deviceId, CancellationToken ct)
    {
        var all = await Devices.Enumerate()
            .OfCategory(DeviceCategory.Camera)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return all.FirstOrDefault(d => d.Id.Value == deviceId);
    }

    /// <summary>
    /// Maps a Periphery camera exception to a <see cref="CameraUnavailableException"/>
    /// whose message the UI turns into one of the four scope Part 1.7 states.
    /// </summary>
    /// <param name="ex">The package exception. Not null.</param>
    /// <returns>A <see cref="CameraUnavailableException"/> preserving the original as inner.</returns>
    private static CameraUnavailableException MapException(Exception ex)
    {
        // WHAT: translate the package's typed exception to a reason string.
        // WHY:  the contract has one exception type; the UI needs the reason.
        // NOT:  not a string-match on message text — the type is the signal.
        if (ex is Periphery.Camera.CameraAccessDeniedException)
            return new CameraUnavailableException("Camera access denied.", ex);
        if (ex is Periphery.Camera.CameraDeviceNotFoundException)
            return new CameraUnavailableException("No camera detected.", ex);
        if (ex is Periphery.Camera.CameraDeviceLostException)
            return new CameraUnavailableException("Camera disconnected.", ex);
        if (ex is Periphery.Camera.CameraTimeoutException)
            return new CameraUnavailableException("Capture failed: timeout.", ex);
        if (ex is Periphery.Camera.CameraConfigurationException)
            return new CameraUnavailableException("Capture failed: configuration.", ex);
        if (ex is Periphery.Camera.CameraTeardownPendingException)
            return new CameraUnavailableException("Camera is stopping; retry.", ex);
        return new CameraUnavailableException("Camera is unavailable.", ex);
    }
}
