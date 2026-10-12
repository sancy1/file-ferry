// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Services/CountingStream.cs
// PURPOSE:  A non-owning write-through decorator that counts the bytes
//           passed to Write and WriteAsync. Used by CloudOutputSink to
//           report bytes written without assuming the upload stream
//           exposes a meaningful Position.
// RELATION: Consumed by CloudOutputSink. Wraps the stream from a
//           CloudWriteHandle. The handle owns the inner stream and
//           disposes it; this decorator does not.
// ============================================================================

namespace FileHarvester.Platform.Cloud.Services;

/// <summary>
/// A write-through stream that counts bytes written. Non-owning: it
/// never disposes the inner stream. The owner of the inner stream (a
/// CloudWriteHandle) is the sole disposer.
/// </summary>
/// <remarks>
/// <para>
/// This decorator is write-only. Read, Seek, SetLength, Length, and
/// Position all throw <see cref="NotSupportedException"/>. The
/// <see cref="CanSeek"/> property reports false regardless of the
/// inner stream's capabilities, so a caller that probes it gets the
/// truthful answer: this stream cannot be rewound.
/// </para>
/// <para>
/// The byte count is incremented only after the inner write completes
/// successfully. A write that throws does not contribute to the count.
/// </para>
/// <para>
/// <see cref="Dispose(bool)"/> and <see cref="DisposeAsync"/> are
/// inert. The inner stream is not touched. The owner disposes it.
/// </para>
/// </remarks>
internal sealed class CountingStream : Stream
{
    private readonly Stream _inner;

    /// <summary>
    /// Wraps the given stream.
    /// </summary>
    /// <param name="inner">
    /// The stream to write through. Must be writable. Never null.
    /// Not required to be readable or seekable.
    /// </param>
    public CountingStream(Stream inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <summary>
    /// The running count of bytes accepted by the inner stream.
    /// Incremented only after a successful write.
    /// </summary>
    public long BytesWritten { get; private set; }

    /// <inheritdoc/>
    public override bool CanRead => false;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => _inner.CanWrite;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void Flush() => _inner.Flush();

    /// <inheritdoc/>
    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _inner.FlushAsync(cancellationToken);

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        _inner.Write(buffer, offset, count);
        BytesWritten += count;
    }

    /// <inheritdoc/>
    public override async Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken)
            .ConfigureAwait(false);
        BytesWritten += count;
    }

    /// <inheritdoc/>
    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        await _inner.WriteAsync(buffer, cancellationToken)
            .ConfigureAwait(false);
        BytesWritten += buffer.Length;
    }

    /// <summary>
    /// Non-owning decorator. Does not dispose the inner stream.
    /// </summary>
    /// <param name="disposing">Unused.</param>
    protected override void Dispose(bool disposing)
    {
        // base.Dispose(disposing) marks the Stream base as disposed.
        // It does NOT dispose the inner stream. The CloudWriteHandle
        // that produced the inner stream is the sole disposer.
        base.Dispose(disposing);
    }

    /// <summary>
    /// Non-owning decorator. Does not dispose the inner stream.
    /// </summary>
    public override ValueTask DisposeAsync()
    {
        // base.DisposeAsync() marks the Stream base as disposed.
        // It does NOT dispose the inner stream. The CloudWriteHandle
        // that produced the inner stream is the sole disposer.
        return base.DisposeAsync();
    }
}
