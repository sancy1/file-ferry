// ============================================================================
// FILE:     src/CodeViewer.Platform/State/IWorkspaceStateStore.cs
// PURPOSE:  Abstraction over per-project workspace persistence. The store
//           never throws to callers: load/save failures are logged and
//           surfaced as null / false so CodeViewer keeps working in memory.
// RELATION: Implemented by JsonWorkspaceStateStore. Consumed by the App
//           view models (Phase 5/8).
// ============================================================================

namespace CodeViewer.Platform.State;

/// <summary>
/// Persists and restores per-project workspace state.
/// </summary>
public interface IWorkspaceStateStore
{
    /// <summary>
    /// Loads the saved state for a project, or null when none exists, the
    /// file is corrupt (quarantined), the schema is unsupported, or
    /// persistence is unavailable. Never throws.
    /// </summary>
    /// <param name="projectPath">The project root path (any form).</param>
    Task<WorkspaceState?> LoadAsync(string projectPath, CancellationToken ct = default);

    /// <summary>
    /// Saves a project's workspace state atomically. Returns false (and
    /// logs) when persistence is unavailable or the write fails. Never
    /// throws.
    /// </summary>
    Task<bool> SaveAsync(WorkspaceState state, CancellationToken ct = default);
}
