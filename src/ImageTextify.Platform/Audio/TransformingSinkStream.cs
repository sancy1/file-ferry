// filepath: src/ImageTextify.Platform/Audio/TransformingSinkStream.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Audio
// since:    Phase 10 Block 10.F.3
// purpose:  A write-only Stream wrapper that receives float32 PCM bytes from
//           TeeStream and converts them to PCM16 bytes before writing to the
//           inner file stream. Used by LiveRecordingService to produce a
//           playable 16 kHz mono PCM16 WAV from the capture stream.
// RELATIONSHIPS
//   Implements : System.IO.Stream
//   Depends on : System.IO.Stream (inner), System.Runtime.InteropServices.MemoryMarshal
//   Used by    : ImageTextify.Platform.Audio.LiveRecordingService
//   See also   : ImageTextify.Platform.Audio.TeeStream
// NOTES
//   - Write-only. CanRead = false, CanSeek = false. Length and Position
//     throw NotSupportedException.
//   - Owns the inner stream: Dispose disposes the inner stream.
//   - Every Write must be float32-aligned (count % 4 == 0). The capture
//     stream emits aligned reads (1600-sample chunks = 6400 bytes) and
//     the tee passes them verbatim. A misaligned write throws
//     InvalidOperationException rather than silently corrupting the file.
//   - The float32 -> PCM16 conversion uses Math.Clamp(v * 32767, -32768, 32767)
//     matching LiveTranscriptionSession.WritePcm16Wav exactly.
//   - Written in little-endian (BitConverter.IsLittleEndian is assumed; on
//     a big-endian platform the conversion would need a byte swap, but no
//     supported platform is big-endian).

using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ImageTextify.Platform.Audio;

internal sealed class TransformingSinkStream : Stream
{
    private readonly Stream _inner;
    private readonly byte[] _scratch;

    public TransformingSinkStream(Stream inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _scratch = ArrayPool<byte>.Shared.Rent(8192);
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        Write(new ReadOnlySpan<byte>(buffer, offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length % 4 != 0)
        {
            throw new InvalidOperationException(
                "TransformingSinkStream requires float32-aligned writes; got " +
                buffer.Length + " bytes.");
        }

        var floats = MemoryMarshal.Cast<byte, float>(buffer);
        int sampleCount = floats.Length;
        int byteCount = sampleCount * 2;

        byte[] target;
        if (_scratch.Length >= byteCount)
        {
            target = _scratch;
        }
        else
        {
            target = new byte[byteCount];
        }

        for (int i = 0; i < sampleCount; i++)
        {
            double v = floats[i] * 32767.0;
            if (v > 32767.0) { v = 32767.0; }
            else if (v < -32768.0) { v = -32768.0; }
            short s = (short)v;
            target[i * 2] = (byte)(s & 0xFF);
            target[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }

        _inner.Write(target, 0, byteCount);
    }

    public override void Flush() => _inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ArrayPool<byte>.Shared.Return(_scratch);
            try { _inner.Dispose(); } catch { }
        }
        base.Dispose(disposing);
    }
}