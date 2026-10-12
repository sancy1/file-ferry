// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/CloudWriteHandle.cs
// PURPOSE:  The handle returned by ICloudProvider.OpenWriteAsync. Owns
//           the upload: the caller writes to Stream, then calls
//           CompleteAsync to commit. Disposing without completing
//           aborts the upload. A sealed class, not a record: it owns
//           a resource.
// RELATION: Produced by ICloudProvider.OpenWriteAsync. Consumed by
//           CloudOutputSink.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// A cloud upload in progress. Write to Stream, then call CompleteAsync
/// to commit, then dispose.
/// </summary>
/// <remarks>
/// <para>
/// The handle owns the commit step. The stream is provided by the
/// provider and must not be closed by the caller before CompleteAsync
/// runs.
/// </para>
/// <para>
/// Disposing without calling CompleteAsync aborts the upload. A
/// CompleteAsync call after dispose throws ObjectDisposedException.
/// </para>
/// </remarks>
public sealed class CloudWriteHandle : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task> _completeAsync;
    private bool _committed;
    private bool _disposed;

    /// <summary>
    /// Creates a write handle over the given stream and commit delegate.
    /// </summary>
    /// <param name="stream">The stream the caller writes to. Never null.</param>
    /// <param name="completeAsync">
    /// The delegate that commits the upload. Never null. Invoked
    /// exactly once, by CompleteAsync.
    /// </param>
    public CloudWriteHandle(
        Stream stream,
        Func<CancellationToken, Task> completeAsync)
    {
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _completeAsync = completeAsync ?? throw new ArgumentNullException(nameof(completeAsync));
    }

    /// <summary>
    /// The writable stream. The caller writes the object bytes here.
    /// The stream may or may not be seekable.
    /// </summary>
    public Stream Stream { get; }

    /// <summary>
    /// Commits the upload. Can be called at most once. Throws
    /// ObjectDisposedException after dispose or after a successful commit.
    /// </summary>
    /// <param name="cancellationToken">Cancels the commit.</param>
    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_committed)
        {
            throw new InvalidOperationException(
                "The upload has already been completed.");
        }

        await _completeAsync(cancellationToken).ConfigureAwait(false);
        _committed = true;
    }

    /// <summary>
    /// Aborts the upload when not committed, then releases the stream.
    /// Safe to call multiple times.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            await Stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best-effort stream cleanup. Do not mask the primary error
            // (if any) by throwing from DisposeAsync.
        }
    }
}
