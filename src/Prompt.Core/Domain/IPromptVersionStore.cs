// ============================================================================
// FILE:     src/Prompt.Core/Domain/IPromptVersionStore.cs
// PURPOSE:  Contract for version snapshots of prompts. Used by the review
//           flow (P4.2) and by SqlitePromptStore (P1.3) to snapshot the
//           previous body on every update.
// RELATION: Implemented by SqlitePromptVersionStore (P1.4). Consumed by
//           SqlitePromptStore.UpdateAsync / PatchAsync (P1.3), and by
//           PromptHistoryViewModel (P4.2).
// NOTES:    - SnapshotAsync has two overloads: the public one opens its own
//             transaction; the (connection, transaction) one is for
//             SqlitePromptStore to call inside its own transaction so the
//             snapshot is coupled to the update (F-003, Mechanism B - F-089).
//           - The snapshot captures Title and Body only.
//           - GetVersionsAsync returns the newest 50 by default; the cap is
//             enforced at snapshot time (delete oldest after insert).
// ============================================================================

namespace Prompt.Core.Domain;

/// <summary>
/// Version snapshots of prompts.
/// </summary>
public interface IPromptVersionStore
{
    /// <summary>
    /// Creates a snapshot of the given prompt. Opens its own connection and
    /// transaction. Callers that already have a transaction should use the
    /// overload that takes one.
    /// </summary>
    Task<PromptVersion> SnapshotAsync(
        Prompt prompt,
        string? note = null,
        CancellationToken cancellationToken = default);


    /// <summary>
    /// Lists snapshots for a prompt, newest first, capped at 50.
    /// </summary>
    Task<IReadOnlyList<PromptVersion>> GetVersionsAsync(
        Guid promptId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a prompt's title and body from the given version. Returns the
    /// updated prompt. Does not create a new snapshot of the pre-restore
    /// state (the review flow at P4.2 handles that).
    /// </summary>
    Task<Prompt> RestoreVersionAsync(
        Guid promptId,
        Guid versionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every snapshot for the prompt. The prompt itself is not
    /// touched; the current title/body remain. The next edit creates the
    /// first new snapshot. Used by the history dialog's Clear History
    /// (P4.2) to start fresh when the history grows too long.
    /// </summary>
    Task DeleteAllVersionsAsync(
        Guid promptId,
        CancellationToken cancellationToken = default);
}