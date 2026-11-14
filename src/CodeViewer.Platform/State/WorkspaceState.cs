// ============================================================================
// FILE:     src/CodeViewer.Platform/State/WorkspaceState.cs
// PURPOSE:  The schema-v1 per-project workspace state persisted to JSON.
//           Records everything needed to restore a project's workspace
//           without re-scanning: expanded folders, checked paths, the
//           selected file, display mode, Auto Display flag, scroll
//           positions, splitter dimensions, and the open count. Source
//           code is NEVER persisted - only paths and UI state.
// RELATION: Serialized by JsonWorkspaceStateStore. Deserialized and
//           applied by the App view models (Phase 5/8) using the D4
//           restore algorithm.
// ============================================================================

using CodeViewer.Core.Domain;

namespace CodeViewer.Platform.State;

/// <summary>
/// The persisted workspace state of one project tab.
/// </summary>
public sealed class WorkspaceState
{
    /// <summary>The current schema version written by this build.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version for forward/backward compatibility.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// The normalized project root path. Must match the key the file was
    /// saved under; a mismatch means the file belongs to another project
    /// and is quarantined rather than applied.
    /// </summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>The project display name (root folder name).</summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>How many times the project has been opened (the "Opened: N times" label).</summary>
    public int OpenedCount { get; set; }

    /// <summary>The active display mode.</summary>
    public DisplayMode DisplayMode { get; set; } = DisplayMode.StructureAndCode;

    /// <summary>Whether Auto Display was on when the workspace was saved.</summary>
    public bool AutoDisplay { get; set; }

    /// <summary>The per-project "Show system files" toggle.</summary>
    public bool ShowSystemFiles { get; set; }

    /// <summary>
    /// Expanded directory paths, relative to the project root. Restored
    /// without a full scan: only these directories are re-enumerated.
    /// </summary>
    public List<string> ExpandedDirectories { get; set; } = new();

    /// <summary>
    /// Checked file and directory paths, relative to the project root.
    /// Restored by statting/re-reading only these paths.
    /// </summary>
    public List<string> CheckedPaths { get; set; } = new();

    /// <summary>The selected file path relative to the root, or null.</summary>
    public string? SelectedFilePath { get; set; }

    /// <summary>Vertical scroll offset of the Project Browser pane.</summary>
    public double BrowserScrollOffset { get; set; }

    /// <summary>Vertical scroll offset of the Project Tree Structure pane.</summary>
    public double StructureScrollOffset { get; set; }

    /// <summary>Vertical scroll offset of the Code Content pane.</summary>
    public double CodeScrollOffset { get; set; }

    /// <summary>
    /// Width of the Project Browser pane in pixels (the left splitter
    /// position). Zero means "use the default".
    /// </summary>
    public double BrowserWidth { get; set; }

    /// <summary>
    /// Proportion of the right-hand workspace given to the Project Tree
    /// Structure pane in Structure + Code mode, 0.0-1.0. Zero means "use
    /// the default".
    /// </summary>
    public double StructureRatio { get; set; }
}
