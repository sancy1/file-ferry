// ============================================================================
// FILE:     src/Prompt.Core/Domain/PromptEventNames.cs
// PURPOSE:  The canonical operation names for the Prompt App's structured
//           application events. These are NOT ResilienceEventType values -
//           the Portfolio.Resilience enum is closed and shared. They are
//           carried on ResilienceEvent.Metadata["operation"] so the Log
//           Drawer and any downstream consumer can distinguish which
//           Prompt operation produced the event.
// RELATION: Consumed by IPromptEventEmitter implementations and by every
//           Prompt store/export/import call site that emits an operation
//           event. Referenced by PromptEventVocabularyTests.
// NOTES:   - The names are stable identifiers. Renaming one is a breaking
//             change for any consumer that filters by name.
//           - The eight names are exactly the plan's P5.1 vocabulary:
//             prompt_created, prompt_updated, prompt_deleted,
//             prompt_restored, prompt_exported, prompt_imported,
//             prompt_search_executed, prompt_render_failed.
//           - These are application identifiers, not library event types.
//             See F-287 in the phase map for the discovery trail.
// ============================================================================

namespace Prompt.Core.Domain;

/// <summary>
/// Canonical operation names for the Prompt App's structured events.
/// </summary>
/// <remarks>
/// These names are set on <c>ResilienceEvent.Metadata["operation"]</c> by the
/// Prompt event emitter. They are deliberately not <c>ResilienceEventType</c>
/// values: the library enum is closed and shared across products, and the
/// Prompt App must not require a library change to add a Prompt operation.
/// </remarks>
public static class PromptEventNames
{
    /// <summary>Emitted when a new prompt is created.</summary>
    public const string Created = "prompt_created";

    /// <summary>Emitted when an existing prompt is updated (PUT or PATCH).</summary>
    public const string Updated = "prompt_updated";

    /// <summary>Emitted when a prompt is soft-deleted.</summary>
    public const string Deleted = "prompt_deleted";

    /// <summary>Emitted when a soft-deleted prompt is restored.</summary>
    public const string Restored = "prompt_restored";

    /// <summary>Emitted when a prompt is exported to a single file or a zip.</summary>
    public const string Exported = "prompt_exported";

    /// <summary>Emitted when a prompt is imported from a file.</summary>
    public const string Imported = "prompt_imported";

    /// <summary>Emitted when a search query is executed against the prompt store.</summary>
    public const string SearchExecuted = "prompt_search_executed";

    /// <summary>Emitted when a prompt body render (for example Markdown to plain text) fails.</summary>
    public const string RenderFailed = "prompt_render_failed";
}