// ============================================================================
// FILE:     src/FileHarvester.Tests/Infrastructure/SyncProgress.cs
// PURPOSE:  A synchronous IProgress<T> implementation for tests. Runs the
//           callback inline on the calling thread so that reported values
//           are observable by the time the producing method returns.
// RELATION: Used by every test that needs to observe progress ticks. The
//           built-in System.Progress<T> dispatches to the thread pool and
//           is unsuitable for synchronous assertions.
// ============================================================================

namespace FileHarvester.Tests.Infrastructure;

/// <summary>
/// A synchronous <see cref="IProgress{T}"/> that runs its callback inline.
/// Use this in tests instead of <see cref="Progress{T}"/>, which dispatches
/// asynchronously and cannot be relied on for immediate observation.
/// </summary>
/// <typeparam name="T">The progress value type.</typeparam>
internal sealed class SyncProgress<T> : IProgress<T>
{
    private readonly Action<T> _callback;

    /// <summary>
    /// Creates a synchronous progress that invokes the callback for every
    /// reported value.
    /// </summary>
    public SyncProgress(Action<T> callback)
    {
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    /// <inheritdoc/>
    public void Report(T value) => _callback(value);
}
