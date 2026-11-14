// ============================================================================
// FILE:     src/Prompt.Core/Domain/PromptVersion.cs
// PURPOSE:  A saved snapshot of a prompt at a point in time. Used by the
//           version-history feature (P4.2) to restore a prior body.
// RELATION: Referenced by IPromptVersionStore (P1.4). Created by
//           SqlitePromptStore.UpdateAsync / PatchAsync before each write.
// NOTES:    The snapshot captures Title and Body only. Category linkage,
//           tags, and other metadata are not part of a version. Restoring a
//           version restores Title and Body; the caller decides whether to
//           also restore the other fields.
// ============================================================================

namespace Prompt.Core.Domain;

/// <summary>
/// A saved snapshot of a prompt's title and body at a point in time.
/// </summary>
/// <remarks>
/// Snapshots are created by <see cref="SqlitePromptStore"/> (P1.3) on every
/// update, capturing the body before the update. Restore (P4.2) reads the
/// snapshot and applies it to the current prompt.
/// </remarks>
public sealed record PromptVersion(
    Guid Id,
    Guid PromptId,
    string Title,
    string Body,
    DateTimeOffset SavedAt,
    string? Note);