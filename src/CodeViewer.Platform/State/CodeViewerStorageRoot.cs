// ============================================================================
// FILE:     src/CodeViewer.Platform/State/CodeViewerStorageRoot.cs
// PURPOSE:  Carries the resolved CodeViewer workspace-state directory from
//           composition to the workspace state store. A null directory means
//           persistence is unavailable and the store must run in memory only.
// RELATION: Created by CodeViewerServiceCollectionExtensions.AddCodeViewer
//           and consumed by the JSON workspace state store (Phase 4).
// ============================================================================

namespace CodeViewer.Platform.State;

/// <summary>
/// The directory CodeViewer owns for per-project workspace state files,
/// or null when the directory could not be created at startup.
/// </summary>
/// <param name="RootDirectory">
/// The absolute path CodeViewer persists workspace state under, or null to
/// run without persistence. When null, state is kept in memory for the
/// session and nothing is written to disk.
/// </param>
public sealed record CodeViewerStorageRoot(string? RootDirectory)
{
    /// <summary>
    /// True when a usable on-disk directory exists and state may be
    /// persisted; false when running in memory only.
    /// </summary>
    public bool IsPersistent => RootDirectory is not null;

    /// <summary>
    /// The in-memory-only instance used when the storage directory cannot
    /// be created (permissions, disk errors, or another filesystem issue).
    /// </summary>
    public static CodeViewerStorageRoot InMemory { get; } = new((string?)null);
}
