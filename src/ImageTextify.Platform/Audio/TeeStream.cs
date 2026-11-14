// filepath: src/ImageTextify.Platform/Audio/TeeStream.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Audio
// since:    Phase 10 Block 10.F.2
// purpose:  A read-only Stream wrapper that reads from an inner stream and
//           mirrors every byte read to a sink stream. Used to record audio
//           while the streaming segmenter consumes it, without changing the
//           segmenter or the capture adapter.
// RELATIONSHIPS
//   Implements : System.IO.Stream
//   Depends on : System.IO.Stream (inner, sink)
//   Used by    : ImageTextify.Platform.Audio.LiveRecordingService
//   See also   : ImageTextify.Platform.Audio.LiveTranscriptionSession
// NOTES
//   - Read-only. CanSeek = false, CanWrite = false. Length and Position
//     throw NotSupportedException.
//   - Ownership: the tee disposes the inner stream on Dispose. It does NOT
//     dispose the sink; the recording service owns the sink and disposes it
//     in StopAsync. This avoids double-dispose.
//   - The sink write is synchronous. If the sink is a FileStream with its
//     own buffering, the write is fast. If profiling shows a stall, the
//     sink should buffer internally; that is a future change, not a Phase
//     10.F concern.
//   - The sink exceptions are swallowed: a recorder failure must not break
//     the transcription. A future amendment can surface sink failures as
//     quality warnings.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ImageTextify.Platform.Audio;

internal sealed class TeeStream : Stream
{
    private readonly Stream _inner;
    private readonly Stream _sink;

    public TeeStream(Stream inner, Stream sink)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(sink);
        _inner = inner;
        _sink = sink;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int n = _inner.Read(buffer, offset, count);
        if (n > 0)
        {
            try { _sink.Write(buffer, offset, n); } catch { }
        }
        return n;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int n = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        if (n > 0)
        {
            try { _sink.Write(buffer, offset, n); } catch { }
        }
        return n;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int n = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (n > 0)
        {
            try { _sink.Write(buffer.Span.Slice(0, n)); } catch { }
        }
        return n;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _inner.Dispose(); } catch { }
        }
        base.Dispose(disposing);
    }
}