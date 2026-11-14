// ============================================================================
// FILE:     src/Prompt.Core/Domain/IPromptStore.cs
// PURPOSE:  Contract for CRUD, soft-delete, restore, and tag operations on
//           prompts. Distinguishes PUT (full replace) from PATCH (partial
//           update via PromptPatch).
// RELATION: Implemented by SqlitePromptStore (P1.3). Consumed by
//           PromptListViewModel (P2.3), PromptEditorViewModel (P2.5), and
//           PromptViewerViewModel (P2.4).
// NOTES:    - UpdateAsync (PUT) replaces every mutable field of the prompt.
//           - PatchAsync (PATCH) updates only the fields with IsSpecified.
//           - HardDeleteAsync is only reachable from the trash view; the
//             normal delete is SoftDeleteAsync.
//           - AttachTagAsync / DetachTagAsync are idempotent (attaching an
//             already-attached tag is a no-op; detaching a missing tag is
//             a no-op).
// ============================================================================

namespace Prompt.Core.Domain;

/// <summary>
/// CRUD, soft-delete, restore, and tag operations on prompts.
/// </summary>
public interface IPromptStore
{
    /// <summary>Creates a new prompt and returns it.</summary>
    Task<Prompt> CreateAsync(Prompt prompt, CancellationToken cancellationToken = default);

    /// <summary>Gets a prompt by id, or null if it does not exist.</summary>
    Task<Prompt?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Replaces a prompt's fields (PUT semantics).</summary>
    Task<Prompt> UpdateAsync(Prompt prompt, CancellationToken cancellationToken = default);

    /// <summary>Patches the specified fields of a prompt (PATCH semantics).</summary>
    Task<Prompt> PatchAsync(Guid id, PromptPatch patch, CancellationToken cancellationToken = default);

    /// <summary>Marks a prompt as deleted (soft delete).</summary>
    Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Restores a soft-deleted prompt.</summary>
    Task RestoreAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes a prompt (only reachable from the trash view).</summary>
    Task HardDeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Attaches a tag to a prompt. Idempotent.</summary>
    Task AttachTagAsync(Guid promptId, string tag, CancellationToken cancellationToken = default);

    /// <summary>Detaches a tag from a prompt. Idempotent.</summary>
    Task DetachTagAsync(Guid promptId, string tag, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches prompts by text (FTS5), scope, category, sub-category, tags,
    /// favorites, and soft-delete inclusion. Paginated by Skip/Take.
    /// </summary>
    Task<IReadOnlyList<Prompt>> SearchAsync(
        PromptQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves (inserts or replaces) the in-progress draft for a prompt.
    /// Keyed by <see cref="PromptDraft.PromptId"/>; a new prompt uses a
    /// temp GUID. Called by the editor's debounced auto-save (P4.3).
    /// </summary>
    Task SaveDraftAsync(PromptDraft draft, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the saved draft for the prompt, or null if none exists.
    /// </summary>
    Task<PromptDraft?> GetDraftAsync(Guid promptId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the saved draft for the prompt. Called on a successful save
    /// or when the user discards the draft.
    /// </summary>
    Task ClearDraftAsync(Guid promptId, CancellationToken cancellationToken = default);
}