// ============================================================================
// filepath: src/FileHarvester.Tests/ImageTextify/CameraCaptureFakes.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 9 (Camera Capture)
// purpose:  Test doubles for ICameraCaptureService and ICameraCaptureSession.
//           Drives the camera slice without a physical webcam.
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.ICameraCaptureService,
//                ImageTextify.Core.Abstractions.ICameraCaptureSession
//   Depends on : n/a (BCL only)
//   Used by    : FileHarvester.Tests.ImageTextify.CameraCaptureTests
//   See also   : CaptureFakes.cs (audio capture fakes, not camera)
// NOTES
//   - Do NOT confuse with CaptureFakes.cs, which is the Phase 10.C.4 AUDIO
//     capture fake. Different interfaces, different file, different concern.
//   - The service tracks enumeration/start counts and last inputs so tests
//     can assert every call.
//   - The session returns a fresh MemoryStream per capture (contract: caller
//     owns the stream). A DisposeStreamTrackingStream wraps it so tests can
//     prove the caller disposed it.
//   - Every async method honors the cancellation token so a cancelled call
//     leaves no partial frame (contract rule; scope Part 6).
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Fake <see cref="ICameraCaptureService"/>. Configurable enumeration,
/// configurable start failure, records every call.
/// </summary>
internal sealed class FakeCameraCaptureService : ICameraCaptureService
{
    /// <summary>Devices returned by every enumeration. Never null; empty default.</summary>
    public IReadOnlyList<CameraDevice> DevicesToReturn { get; set; } = Array.Empty<CameraDevice>();

    /// <summary>When non-null, <see cref="StartAsync"/> throws this.</summary>
    public Exception? ExceptionToThrowOnStart { get; set; }

    /// <summary>Session returned by <see cref="StartAsync"/> when no exception is configured. Never null.</summary>
    public FakeCameraCaptureSession Session { get; }

    /// <summary>Times <see cref="EnumerateAsync"/> was called.</summary>
    public int EnumerateCallCount { get; private set; }

    /// <summary>Times <see cref="StartAsync"/> was called.</summary>
    public int StartCallCount { get; private set; }

    /// <summary>The last device passed to <see cref="StartAsync"/>.</summary>
    public CameraDevice? LastDevice { get; private set; }

    /// <summary>The last options passed to <see cref="StartAsync"/>.</summary>
    public CameraCaptureOptions? LastOptions { get; private set; }

    /// <summary>Creates the service with the given session returned on start.</summary>
    /// <param name="session">The fake session. Not null.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="session"/> is null.</exception>
    public FakeCameraCaptureService(FakeCameraCaptureSession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<CameraDevice>> EnumerateAsync(CancellationToken cancellationToken)
    {
        EnumerateCallCount++;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(DevicesToReturn);
    }

    /// <inheritdoc/>
    public Task<ICameraCaptureSession> StartAsync(
        CameraDevice device,
        CameraCaptureOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(options);
        StartCallCount++;
        LastDevice = device;
        LastOptions = options;
        cancellationToken.ThrowIfCancellationRequested();
        if (ExceptionToThrowOnStart is not null) throw ExceptionToThrowOnStart;
        return Task.FromResult<ICameraCaptureSession>(Session);
    }
}

/// <summary>
/// Fake <see cref="ICameraCaptureSession"/>. Returns a fresh tracked
/// <see cref="MemoryStream"/> per capture. Records stop and dispose.
/// </summary>
internal sealed class FakeCameraCaptureSession : ICameraCaptureSession
{
    private readonly byte[] _frameBytes;
    private readonly int _width;
    private readonly int _height;

    /// <summary>True while active. Set false by StopAsync or DisposeAsync.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Times <see cref="CaptureFrameAsync"/> was called.</summary>
    public int CaptureFrameCallCount { get; private set; }

    /// <summary>Set true when <see cref="StopAsync"/> was called.</summary>
    public bool StopCalled { get; private set; }

    /// <summary>Set true when <see cref="DisposeAsync"/> was called.</summary>
    public bool Disposed { get; private set; }

    /// <summary>The last stream handed to the caller. Null until the first capture.</summary>
    public DisposeStreamTrackingStream? LastStream { get; private set; }

    /// <summary>Creates the fake session.</summary>
    /// <param name="frameBytes">Bytes returned in the frame stream. Not null.</param>
    /// <param name="width">Reported frame width in pixels.</param>
    /// <param name="height">Reported frame height in pixels.</param>
    public FakeCameraCaptureSession(byte[] frameBytes, int width, int height)
    {
        _frameBytes = frameBytes ?? throw new ArgumentNullException(nameof(frameBytes));
        _width = width;
        _height = height;
    }

    /// <inheritdoc/>
    public Task<CapturedFrame> CaptureFrameAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsActive) throw new InvalidOperationException("Session is not active.");
        CaptureFrameCallCount++;
        // Fresh stream per call (contract). The caller owns it.
        var inner = new MemoryStream(_frameBytes, writable: false);
        LastStream = new DisposeStreamTrackingStream(inner);
        return Task.FromResult(new CapturedFrame(
            FrameStream: LastStream,
            Width: _width,
            Height: _height,
            CapturedAtUtc: DateTimeOffset.UtcNow));
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopCalled = true;
        IsActive = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        IsActive = false;
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A read-only stream wrapper that records whether it was disposed. Used to
/// prove the caller disposes the frame stream on discard.
/// </summary>
internal sealed class DisposeStreamTrackingStream : Stream
{
    private readonly Stream _inner;

    /// <summary>True after <see cref="Dispose(bool)"/> has run.</summary>
    public bool WasDisposed { get; private set; }

    /// <summary>Wraps an inner stream.</summary>
    /// <param name="inner">The stream to wrap. Not null.</param>
    public DisposeStreamTrackingStream(Stream inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <inheritdoc/>
    public override bool CanRead => _inner.CanRead;
    /// <inheritdoc/>
    public override bool CanSeek => _inner.CanSeek;
    /// <inheritdoc/>
    public override bool CanWrite => false;
    /// <inheritdoc/>
    public override long Length => _inner.Length;
    /// <inheritdoc/>
    public override long Position { get => _inner.Position; set => _inner.Position = value; }
    /// <inheritdoc/>
    public override void Flush() => _inner.Flush();
    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing) { WasDisposed = true; _inner.Dispose(); }
        base.Dispose(disposing);
    }
}
