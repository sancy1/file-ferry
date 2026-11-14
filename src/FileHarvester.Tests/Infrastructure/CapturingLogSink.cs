// ============================================================================
// FILE:     src/FileHarvester.Tests/Infrastructure/CapturingLogSink.cs
// PURPOSE:  Test-only ILogSink that records every emitted ResilienceEvent
//           so tests can assert that the resilience pipeline produced
//           events for a given operation. Bounded, thread-safe, never
//           throws. Passed to AddFileHarvester via its extraSink parameter.
// RELATION: Test infrastructure. Never referenced by production code.
// ============================================================================

using System.Collections.Concurrent;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Events;

namespace FileHarvester.Tests.Infrastructure;

/// <summary>
/// A test-only log sink that captures every emitted resilience event.
/// </summary>
/// <remarks>
/// <para>
/// Bounded to <see cref="MaxEvents"/>; when the bound is reached, the
/// oldest event is dropped to make room. Thread-safe. <see cref="Emit"/>
/// never throws, so a test can never accidentally fail through the sink.
/// </para>
/// <para>
/// Used only by tests. Not referenced by any production project.
/// </para>
/// </remarks>
public sealed class CapturingLogSink : ILogSink
{
    /// <summary>
    /// Maximum number of events retained in memory.
    /// </summary>
    public const int MaxEvents = 1000;

    private readonly ConcurrentQueue<ResilienceEvent> _events = new();
    private readonly object _gate = new();

    /// <inheritdoc/>
    public void Emit(ResilienceEvent evt)
    {
        if (evt is null) return;

        try
        {
            _events.Enqueue(evt);

            // Trim to bound under the lock.
            lock (_gate)
            {
                while (_events.Count > MaxEvents)
                {
                    _events.TryDequeue(out _);
                }
            }
        }
        catch
        {
            // A test sink must never throw into the pipeline.
        }
    }

    /// <summary>
    /// Returns an immutable snapshot of the captured events, oldest first.
    /// </summary>
    public IReadOnlyList<ResilienceEvent> Snapshot() =>
        _events.ToArray();

    /// <summary>
    /// Removes every captured event.
    /// </summary>
    public void Clear()
    {
        while (_events.TryDequeue(out _)) { }
    }
}