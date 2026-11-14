// filepath: src/CodeViewer.Platform/State/GlobalExclusionsHolder.cs
// ============================================================================
// PURPOSE:  Mutable, thread-safe holder for the current GlobalExclusions
//           instance. Its single responsibility is to let the Manage
//           Exclusions flow publish a new rule set to every open project
//           tab at once, without a direct reference from the view model to
//           each tab. Tabs subscribe to Changed and refresh themselves.
// RELATION: Registered as a singleton by AddCodeViewer. Consumed by
//           CodeViewerViewModel (writes on Save & Apply) and by every
//           ProjectTabViewModel (reads Current on every enumeration, and
//           subscribes to Changed to refresh on updates).
// ============================================================================

using CodeViewer.Core.Domain;

namespace CodeViewer.Platform.State;

/// <summary>
/// A live holder for the current global exclusion rules.
/// </summary>
/// <remarks>
/// The holder itself is a stable singleton; the <see cref="Current"/> set
/// is immutable and replaced atomically on change. Subscribers use the
/// <see cref="Changed"/> event to be notified; the event fires on the
/// thread that called the setter. Consumers that touch UI from the event
/// must marshal to the UI thread themselves.
/// </remarks>
public sealed class GlobalExclusionsHolder
{
    private readonly object _gate = new();
    private GlobalExclusions _current = GlobalExclusions.Empty;

    /// <summary>
    /// Raised after <see cref="Current"/> has been replaced. Subscribers
    /// receive the new value as the event argument.
    /// </summary>
    public event EventHandler<GlobalExclusions>? Changed;

    /// <summary>
    /// The current rule set. Immutable; safely snapshotted by callers.
    /// </summary>
    public GlobalExclusions Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// Replaces the current rule set and raises <see cref="Changed"/>.
    /// A call with the identical instance is a no-op (no event fired).
    /// </summary>
    public void Replace(GlobalExclusions rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        GlobalExclusions? previous = null;
        lock (_gate)
        {
            if (ReferenceEquals(_current, rules))
            {
                return;
            }

            previous = _current;
            _current = rules;
        }

        // Fire outside the lock so subscribers cannot deadlock by
        // reentering Current.
        if (!ReferenceEquals(previous, rules))
        {
            Changed?.Invoke(this, rules);
        }
    }
}