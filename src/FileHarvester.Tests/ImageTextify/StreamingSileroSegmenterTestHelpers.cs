// filepath: src/FileHarvester.Tests/ImageTextify/StreamingSileroSegmenterTestHelpers.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 10 Block 10.B.4
// purpose:  Test-only helpers for StreamingSileroSegmenterTests: a fake
//           IVadSession that returns a scripted sequence of VadSegmentData
//           lists, and a synthetic readable Stream that yields fixed-size
//           chunks of float32 silence.
// RELATIONSHIPS
//   Implements : ImageTextify.Platform.Audio.IVadSession (FakeVadSession),
//                System.IO.Stream (SyntheticAudioStream)
//   Depends on : Whisper.net (VadSegmentData), IVadSession
//   Used by    : FileHarvester.Tests.ImageTextify.StreamingSileroSegmenterTests
//   See also   : ImageTextify.Platform.Audio.StreamingSileroSegmenter
// NOTES
//   - FakeVadSession is scripted: each DetectSpeechNoResetAsync call
//     dequeues the next IReadOnlyList<VadSegmentData> from the queue. If
//     the queue is empty the call returns an empty list. This lets a test
//     express either NoReset semantic (growing list, or new-segments-only)
//     as a scripted sequence.
//   - SyntheticAudioStream.Read fills the caller buffer with zeros, which
//     is float32 silence. The segmenter does not care about audio content
//     when the VAD is faked; only the chunk count matters.
//   - ResetState is recorded (call count exposed) so tests can assert it
//     was invoked at utterance end.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Platform.Audio;
using Whisper.net;

namespace FileHarvester.Tests.ImageTextify;

internal sealed class FakeVadSession : IVadSession
{
    private readonly Queue<IReadOnlyList<VadSegmentData>> _scripted = new();

    public int ResetStateCallCount { get; private set; }

    public FakeVadSession Script(params IReadOnlyList<VadSegmentData>[] results)
    {
        foreach (var r in results) { _scripted.Enqueue(r); }
        return this;
    }

    public Task<IReadOnlyList<VadSegmentData>> DetectSpeechNoResetAsync(
        ReadOnlyMemory<float> samples, CancellationToken cancellationToken)
    {
        IReadOnlyList<VadSegmentData> result = _scripted.Count > 0
            ? _scripted.Dequeue()
            : Array.Empty<VadSegmentData>();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<VadSegmentData>> DetectSpeechAsync(Stream waveStream, CancellationToken cancellationToken)
    {
        IReadOnlyList<VadSegmentData> result = _scripted.Count > 0
            ? _scripted.Dequeue()
            : Array.Empty<VadSegmentData>();
        return Task.FromResult(result);
    }

    public void ResetState() => ResetStateCallCount++;

    public void Dispose() { }
}

internal sealed class SyntheticAudioStream : Stream
{
    private readonly int _chunkBytes;
    private readonly int _maxChunks;
    private int _servedChunks;

    public SyntheticAudioStream(int chunkBytes, int maxChunks)
    {
        _chunkBytes = chunkBytes;
        _maxChunks = maxChunks;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_servedChunks >= _maxChunks) { return 0; }
        int toCopy = Math.Min(count, _chunkBytes);
        Array.Clear(buffer, offset, toCopy);
        _servedChunks++;
        return toCopy;
    }
}