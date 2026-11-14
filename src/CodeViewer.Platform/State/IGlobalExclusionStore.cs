// filepath: src/CodeViewer.Platform/State/IGlobalExclusionStore.cs
// ============================================================================
// PURPOSE:  Abstraction over global exclusion-rule persistence. Mirrors the
//           IWorkspaceStateStore contract: load and save never throw to
//           callers. Load returns Empty on missing / corrupt / unsupported /
//           unavailable configurations. Save returns false on failure.
// RELATION: Implemented by JsonGlobalExclusionStore. Consumed by
//           CodeViewerViewModel at startup (load) and by the Manage
//           Exclusions flow on Save & Apply (save).
// ============================================================================

using CodeViewer.Core.Domain;

namespace CodeViewer.Platform.State;

/// <summary>
/// Persists and restores the global exclusion rule set.
/// </summary>
/// <remarks>
/// The rule set is global to CodeViewer, not per-project. It survives
/// application restarts and is never deleted except when the user removes
/// an individual rule and saves.
/// </remarks>
public interface IGlobalExclusionStore
{
    /// <summary>
    /// Loads the current global exclusion rules. Never throws. Returns
    /// <see cref="GlobalExclusions.Empty"/> when the configuration file is
    /// missing, corrupt (the file is quarantined, not deleted), written by
    /// an unsupported schema version, or when persistence is unavailable.
    /// </summary>
    Task<GlobalExclusions> LoadAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves the rule set atomically. Never throws. Returns false (and logs)
    /// when persistence is unavailable or the write fails. When persistence
    /// is unavailable, the previous saved state (if any) is untouched, so
    /// cancel-or-fail never destroys user data.
    /// </summary>
    /// <remarks>
    /// Before each successful write, the current file (if any) is copied to
    /// <c>global-exclusions.backup.json</c>. This gives a one-generation
    /// recovery point if the user accidentally removed the wrong rule.
    /// </remarks>
    Task<bool> SaveAsync(GlobalExclusions rules, CancellationToken ct = default);
}