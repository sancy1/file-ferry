// ============================================================================
// FILE:     src/Prompt.Platform/Storage/IPromptVersionStoreTransactional.cs
// PURPOSE:  Platform-layer companion interface for IPromptVersionStore. Holds
//           the SQLite-typed SnapshotAsync overload that SqlitePromptStore
//           calls inside its own transaction. Kept out of Prompt.Core so the
//           domain interface does not leak Microsoft.Data.Sqlite types (F-091,
//           F-092).
// RELATION: Implemented by SqlitePromptVersionStore (P1.4) alongside
//           IPromptVersionStore. Consumed by SqlitePromptStore.UpdateAsync and
//           PatchAsync (P1.3) via a cast from IPromptVersionStore.
// NOTES:    - This is a platform-layer interface. It references SQLite types.
//             Domain-layer code must not consume it.
//           - The cast in SqlitePromptStore assumes the injected
//             IPromptVersionStore also implements this interface. In production
//             that is SqlitePromptVersionStore; in tests the StubVersionStore
//             implements both.
//           - Keeping the two interfaces separate is the point: the domain
//             contract is SQLite-agnostic; the transactional contract is a
//             platform concern.
// ============================================================================

using Microsoft.Data.Sqlite;

namespace Prompt.Platform.Storage;

/// <summary>
/// The transactional companion to <see cref="Prompt.Core.Domain.IPromptVersionStore"/>.
/// Adds the in-transaction snapshot overload that uses SQLite types.
/// </summary>
public interface IPromptVersionStoreTransactional
{
    /// <summary>
    /// Creates a snapshot inside the caller's transaction. The snapshot is
    /// committed or rolled back with the update.
    /// </summary>
    Task<Prompt.Core.Domain.PromptVersion> SnapshotAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Prompt.Core.Domain.Prompt prompt,
        string? note,
        CancellationToken cancellationToken);
}