// ============================================================================
// FILE:     src\FileHarvester.App/Services/ObservableLogSink.cs
// PURPOSE:  ILogSink implementation that bridges Portfolio.Resilience
//           events into the UI thread. Events emitted on background threads
//           are marshalled to the Avalonia dispatcher and appended to an
//           observable collection that the Log drawer binds to.
// RELATION: Registered as a singleton in App.axaml.cs and injected into
//           AddPortfolioResilience alongside ConsoleLogSink and FileLogSink.
//           Consumed by LogDrawerViewModel, which wraps each event in a
//           LogEntryViewModel.
// ============================================================================

using System.Collections.ObjectModel;
using Avalonia.Threading;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Events;

namespace FileHarvester.App.Services;

/// <summary>
/// A log sink that routes Portfolio.Resilience events to the UI thread.
/// </summary>
/// <remarks>
/// Bounded to <see cref="MaxEvents"/> entries. When full, the oldest event
/// is removed to make room for the new one. This keeps the memory footprint
/// constant during multi-hour harvest jobs.
///
/// Emit may be called from any thread. Every event is marshalled to the
/// Avalonia UI thread via Dispatcher.UIThread.Post before the collection is
/// touched, so the ObservableCollection is only ever mutated on the UI
/// thread. This is required by Avalonia (and by any UI framework).
/// </remarks>
public sealed class ObservableLogSink : ILogSink
{
    /// <summary>
    /// Maximum number of events retained in memory. Older events are
    /// dropped when the buffer is full.
    /// </summary>
    public const int MaxEvents = 1000;

    /// <summary>
    /// Raised on the UI thread whenever a new event is appended. Lets the
    /// view model apply auto-scroll without polling.
    /// </summary>
    public event EventHandler<ResilienceEvent>? EventAppended;

    /// <summary>
    /// The current buffer of events, oldest first, newest last.
    /// Bound to by LogDrawerViewModel.
    /// </summary>
    public ObservableCollection<ResilienceEvent> Events { get; } = new();

    /// <inheritdoc/>
    public void Emit(ResilienceEvent evt)
    {
        if (evt is null)
        {
            return;
        }

        // Marshal to the UI thread. Dispatcher.UIThread.Post never blocks
        // and is safe to call from any thread.
        Dispatcher.UIThread.Post(() =>
        {
            // Enforce the bound.
            while (Events.Count >= MaxEvents)
            {
                Events.RemoveAt(0);
            }

            Events.Add(evt);
            EventAppended?.Invoke(this, evt);
        });
    }

    /// <summary>
    /// Clears every event. Must be called on the UI thread.
    /// </summary>
    public void Clear()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Events.Clear();
        }
        else
        {
            Dispatcher.UIThread.Post(() => Events.Clear());
        }
    }
}
