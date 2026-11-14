// ============================================================================
// FILE:     src/Prompt.Core/Domain/PromptDraft.cs
// PURPOSE:  An auto-saved in-progress edit of a prompt, used for crash
//           recovery (P4.3). Distinct from PromptVersion: drafts are volatile
//           and discarded on successful save; versions are permanent.
// RELATION: Referenced by IPromptStore's draft helpers (P1.3). Written by
//           PromptEditorViewModel's debounced auto-save (P4.3).
// NOTES:    A draft is keyed by PromptId (a new prompt uses a temp GUID).
//           Title and Body are nullable because a draft may have been created
//           before the user typed anything.
// ============================================================================

namespace Prompt.Core.Domain;

/// <summary>
/// An auto-saved in-progress edit of a prompt, for crash recovery.
/// </summary>
/// <remarks>
/// Drafts are volatile. A successful save removes the draft; a discard clears
/// it. Version snapshots (see <see cref="PromptVersion"/>) are the permanent
/// record; drafts are not.
/// </remarks>
public sealed record PromptDraft(
    Guid PromptId,
    string? Title,
    string? Body,
    DateTimeOffset SavedAt);