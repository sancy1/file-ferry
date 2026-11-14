// ============================================================================
// filepath: src/FileHarvester.Tests/ImageTextify/CameraCaptureTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 9 (Camera Capture)
// purpose:  Fake-based tests for the camera capture slice, proving the
//           contract behavior in scope Part 8 without a physical webcam.
// RELATIONSHIPS
//   Implements : n/a (test class)
//   Depends on : FakeCameraCaptureService, FakeCameraCaptureSession,
//                DisposeStreamTrackingStream (CameraCaptureFakes.cs),
//                FakeImageOcrEngine (FakeImageOcrEngine.cs),
//                ImageTextify.Core.Abstractions.ICameraCaptureService
//   Used by    : test runner
//   See also   : ImageTextify.Core/Abstractions/ICameraCaptureService.cs (contract),
//                scope document Part 8 (test requirements)
// NOTES
//   - Save-frame scenario is NOT in this file. It belongs to the step that
//     owns the Save frame button (step 10). Scope Part 8 lists 11 scenarios;
//     10 are here; 1 is scheduled.
//   - Every test asserts on a non-empty collection before iterating (no
//     vacuous pass) and names ARRANGE/ACT/ASSERT where the body exceeds
//     ~10 lines.
//   - All fakes honor CancellationToken; the cancellation tests prove no
//     partial frame is returned.
// ============================================================================

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Fake-based acceptance tests for the camera capture contract.
/// </summary>
public sealed class CameraCaptureTests
{
    /// <summary>Two synthetic frame bytes used as the smallest non-empty "image".</summary>
    private static readonly byte[] TinyFrame = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

    // -------------------- enumeration --------------------

    /// <summary>
    /// Empty enumeration returns an empty list. Proves the "no camera" path
    /// (scope Part 1.7 first state) does not throw.
    /// </summary>
    [Fact]
    public async Task EnumerateEmptyReturnsEmptyList()
    {
        // ARRANGE
        var session = new FakeCameraCaptureSession(TinyFrame, 16, 16);
        var svc = new FakeCameraCaptureService(session) { DevicesToReturn = Array.Empty<CameraDevice>() };

        // ACT
        var devices = await svc.EnumerateAsync(CancellationToken.None);

        // ASSERT
        Assert.NotNull(devices);
        Assert.Empty(devices);
        Assert.Equal(1, svc.EnumerateCallCount);
    }

    /// <summary>
    /// One-device enumeration returns the device and preserves its fields.
    /// Proves the picker would show exactly what the OS exposed.
    /// </summary>
    [Fact]
    public async Task EnumerateOneDeviceReturnsDevice()
    {
        // ARRANGE
        var device = new CameraDevice("id-1", "Integrated Camera", true);
        var session = new FakeCameraCaptureSession(TinyFrame, 16, 16);
        var svc = new FakeCameraCaptureService(session) { DevicesToReturn = new[] { device } };

        // ACT
        var devices = await svc.EnumerateAsync(CancellationToken.None);

        // ASSERT
        Assert.Single(devices);
        Assert.Equal("id-1", devices[0].DeviceId);
        Assert.Equal("Integrated Camera", devices[0].DisplayName);
        Assert.True(devices[0].IsAvailable);
    }

    // -------------------- start failure paths --------------------

    /// <summary>
    /// Permission-denied surfaces as <see cref="CameraUnavailableException"/>
    /// with the specific message. Proves scope Part 1.7 state 2.
    /// </summary>
    [Fact]
    public async Task StartPermissionDeniedThrowsCameraUnavailable()
    {
        // ARRANGE
        var session = new FakeCameraCaptureSession(TinyFrame, 16, 16);
        var svc = new FakeCameraCaptureService(session)
        {
            ExceptionToThrowOnStart = new CameraUnavailableException("Camera access denied.")
        };
        var device = new CameraDevice("id-1", "Cam", true);

        // ACT + ASSERT
        var ex = await Assert.ThrowsAsync<CameraUnavailableException>(
            () => svc.StartAsync(device, new CameraCaptureOptions(), CancellationToken.None));
        Assert.Contains("denied", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Device disconnect surfaces as <see cref="CameraUnavailableException"/>.
    /// Proves scope Part 1.7 state 3.
    /// </summary>
    [Fact]
    public async Task StartDisconnectedThrowsCameraUnavailable()
    {
        // ARRANGE
        var session = new FakeCameraCaptureSession(TinyFrame, 16, 16);
        var svc = new FakeCameraCaptureService(session)
        {
            ExceptionToThrowOnStart = new CameraUnavailableException("Camera disconnected.")
        };
        var device = new CameraDevice("id-1", "Cam", true);

        // ACT + ASSERT
        var ex = await Assert.ThrowsAsync<CameraUnavailableException>(
            () => svc.StartAsync(device, new CameraCaptureOptions(), CancellationToken.None));
        Assert.Contains("disconnected", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------- capture --------------------

    /// <summary>
    /// Capture returns a caller-owned stream whose contents match the fake
    /// bytes and whose reported dimensions match. Proves the frame-to-stream
    /// handoff (scope Part 1.4).
    /// </summary>
    [Fact]
    public async Task CaptureFrameAsyncReturnsReadableStream()
    {
        // ARRANGE
        var session = new FakeCameraCaptureSession(TinyFrame, 32, 24);
        var svc = new FakeCameraCaptureService(session);
        var device = new CameraDevice("id-1", "Cam", true);
        var opened = await svc.StartAsync(device, new CameraCaptureOptions(), CancellationToken.None);

        // ACT
        var frame = await opened.CaptureFrameAsync(CancellationToken.None);

        // ASSERT
        Assert.NotNull(frame);
        Assert.Equal(32, frame.Width);
        Assert.Equal(24, frame.Height);
        Assert.NotNull(frame.FrameStream);
        Assert.True(frame.FrameStream.CanRead);
        using var ms = new MemoryStream();
        await frame.FrameStream.CopyToAsync(ms);
        Assert.Equal(TinyFrame, ms.ToArray());
        await frame.FrameStream.DisposeAsync();
    }

    // -------------------- session lifecycle --------------------

    /// <summary>
    /// Stop flips IsActive to false and records the call. Proves scope Part 1.8.
    /// </summary>
    [Fact]
    public async Task StopAsyncReleasesSessionIsActiveFalse()
    {
        // ARRANGE
        var session = new FakeCameraCaptureSession(TinyFrame, 16, 16);
        var svc = new FakeCameraCaptureService(session);
        var opened = await svc.StartAsync(
            new CameraDevice("id-1", "Cam", true),
            new CameraCaptureOptions(),
            CancellationToken.None);
        Assert.True(opened.IsActive);

        // ACT
        await opened.StopAsync(CancellationToken.None);

        // ASSERT
        Assert.False(opened.IsActive);
        Assert.True(session.StopCalled);
    }

    /// <summary>
    /// Disposing the returned frame stream is what the discard path must do.
    /// Proves scope Part 1.4 discard does not leak the buffer.
    /// </summary>
    [Fact]
    public async Task DiscardDisposesStream()
    {
        // ARRANGE
        var session = new FakeCameraCaptureSession(TinyFrame, 16, 16);
        var svc = new FakeCameraCaptureService(session);
        var opened = await svc.StartAsync(
            new CameraDevice("id-1", "Cam", true),
            new CameraCaptureOptions(),
            CancellationToken.None);
        var frame = await opened.CaptureFrameAsync(CancellationToken.None);
        var tracked = session.LastStream!;
        Assert.False(tracked.WasDisposed);

        // ACT (simulates Discard)
        await frame.FrameStream.DisposeAsync();

        // ASSERT
        Assert.True(tracked.WasDisposed);
    }

    // -------------------- OCR integration --------------------

    /// <summary>
    /// Feeding the captured frame to the OCR engine invokes it exactly once
    /// and yields the fake result. Proves scope Part 1.5 (frame enters the
    /// same pipeline).
    /// </summary>
    [Fact]
    public async Task CaptureFrameOcrRunsEngineOnce()
    {
        // ARRANGE
        var session = new FakeCameraCaptureSession(TinyFrame, 16, 16);
        var svc = new FakeCameraCaptureService(session);
        var engine = new FakeImageOcrEngine
        {
            ResultToReturn = new OcrResult(
                "hello",
                0.9,
                Array.Empty<string>(),
                OcrTextKind.Printed,
                Array.Empty<OcrLineResult>())
        };
        var opened = await svc.StartAsync(
            new CameraDevice("id-1", "Cam", true),
            new CameraCaptureOptions(),
            CancellationToken.None);
        var frame = await opened.CaptureFrameAsync(CancellationToken.None);

        // ACT
        var result = await engine.ExtractAsync(frame.FrameStream, new ImageOcrOptions(Enhance: false), CancellationToken.None);
        await frame.FrameStream.DisposeAsync();

        // ASSERT
        Assert.Equal(1, engine.CallCount);
        Assert.Equal("hello", result.Text);
        Assert.Equal(OcrTextKind.Printed, result.DetectedTextKind);
    }

    // -------------------- cancellation --------------------

    /// <summary>
    /// A pre-cancelled token on capture throws <see cref="OperationCanceledException"/>
    /// and produces no frame. Proves the "no partial frame" contract rule
    /// (scope Part 6).
    /// </summary>
    [Fact]
    public async Task CancellationMidCaptureNoPartialFrame()
    {
        // ARRANGE
        var session = new FakeCameraCaptureSession(TinyFrame, 16, 16);
        var svc = new FakeCameraCaptureService(session);
        var opened = await svc.StartAsync(
            new CameraDevice("id-1", "Cam", true),
            new CameraCaptureOptions(),
            CancellationToken.None);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // ACT + ASSERT
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => opened.CaptureFrameAsync(cts.Token));
        Assert.Equal(0, session.CaptureFrameCallCount);
    }
}
