// filepath: src/FileHarvester.Tests/ImageTextify/CaptureFakes.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 10 Block 10.C.4
// purpose:  Test fakes for the two capture interfaces IAudioCaptureService
//           and IAudioCaptureSession. Used by LiveTranscriptionServiceTests
//           to drive the live orchestrator without a real microphone.
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.IAudioCaptureService,
//                ImageTextify.Core.Abstractions.IAudioCaptureSession
//   Depends on : SyntheticAudioStream (StreamingSileroSegmenterTestHelpers)
//   Used by    : FileHarvester.Tests.ImageTextify.LiveTranscriptionServiceTests
//   See also   : ImageTextify.Platform.Audio.NAudioAudioCaptureService
// NOTES
//   - Records the AudioCaptureOptions it was called with so tests can
//     assert 16 kHz mono was requested.
//   - Records that DisposeAsync was called on the session so tests can
//     assert the service disposes it.
//   - AudioStream yields a SyntheticAudioStream that ends after N chunks.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;

namespace FileHarvester.Tests.ImageTextify;

internal sealed class FakeAudioCaptureService : IAudioCaptureService
{
    public FakeAudioCaptureSession Session { get; }
    public AudioCaptureOptions? LastOptions { get; private set; }
    public int StartCallCount { get; private set; }

    public FakeAudioCaptureService(FakeAudioCaptureSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Session = session;
    }

    public Task<IAudioCaptureSession> StartAsync(AudioCaptureOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastOptions = options;
        StartCallCount++;
        return Task.FromResult<IAudioCaptureSession>(Session);
    }
}

internal sealed class FakeAudioCaptureSession : IAudioCaptureSession
{
    private readonly SyntheticAudioStream _stream;

    public FakeAudioCaptureSession(int chunkBytes, int maxChunks)
    {
        _stream = new SyntheticAudioStream(chunkBytes, maxChunks);
    }

    public bool IsActive { get; private set; } = true;
    public Stream AudioStream => _stream;
    public bool Disposed { get; private set; }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsActive = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsActive = false;
        Disposed = true;
        _stream.Dispose();
        return ValueTask.CompletedTask;
    }
}