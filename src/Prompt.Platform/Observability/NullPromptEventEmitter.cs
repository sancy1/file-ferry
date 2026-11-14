// ============================================================================
// FILE:     src/Prompt.Platform/Observability/NullPromptEventEmitter.cs
// PURPOSE:  A no-op IPromptEventEmitter. Used as the safe fallback when no
//           ILogSink is supplied to AddPromptStores, so the stores can emit
//           unconditionally without null checks and without a live sink the
//           event is silently discarded (which is the intended behaviour
//           for a composition with no log drawer).
// RELATION: Implements IPromptEventEmitter. Registered by
//           PromptStoreServiceCollectionExtensions.AddPromptStores when the
//           caller supplies no log sink; replaced by PromptEventEmitter when
//           a sink is present. Referenced by store constructors via the
//           IPromptEventEmitter injection point.
// NOTES:   - Emit is intentionally empty: a composition without a sink has
//             no observer for the event, and inventing a throw or a log
//             fallback here would change the store's failure semantics. The
//             no-op is deliberate and must not be "improved" into a logger.
//           - This type exists precisely to keep the store constructors
//             free of optional/null branches. The stores call
//             _events.Emit(...) unconditionally.
// ============================================================================

namespace Prompt.Platform.Observability;

/// <summary>
/// No-op <see cref="IPromptEventEmitter"/> used when no log sink is configured.
/// </summary>
/// <remarks>
/// This implementation discards every event. It exists so that the store
/// classes can emit unconditionally without a null check on the emitter.
/// </remarks>
public sealed class NullPromptEventEmitter : IPromptEventEmitter
{
    /// <summary>
    /// A shared, stateless instance. Safe to reuse: the type has no state.
    /// </summary>
    public static readonly NullPromptEventEmitter Instance = new();

    /// <inheritdoc />
    public void Emit(string operation, Guid? promptId = null, string? correlationId = null)
    {
        // Deliberately empty: see the file header NOTES.
    }
}