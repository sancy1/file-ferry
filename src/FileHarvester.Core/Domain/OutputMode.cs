// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/OutputMode.cs
// PURPOSE:  The two top-level output modes File-Ferry supports. Chosen by
//           the user in the Output Mode Selector (Phase 3.3) and read by
//           IBundleComposer (Phase 1.3) and IOutputSink (Phase 6.0).
// RELATION: A first-class user decision, not a setting. Every harvest job
//           commits to exactly one mode before it starts. See the design
//           plan section "Two Core Output Modes" for the full rationale.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// How harvested files are delivered to the user.
/// </summary>
public enum OutputMode
{
    /// <summary>
    /// One output file per source file. The output file name mirrors the
    /// source name with the user-chosen extension. Optional mirrored folder
    /// structure.
    /// </summary>
    /// <example>
    /// src/Program.cs       →  out/src/Program.md
    /// src/Config/app.json  →  out/src/Config/app.md
    /// README.md            →  out/README.md
    /// </example>
    SeparateFiles = 0,

    /// <summary>
    /// One output file containing every source file's contents, each
    /// preceded by a delimiter header. Matches the format of the
    /// inspiration script (resilience-handoff-bundle.txt) with the
    /// default delimiter "===== {path} =====" and two blank lines between
    /// entries.
    /// </summary>
    /// <example>
    /// ===== src/Program.cs =====
    ///
    /// using System;
    /// class Program { ... }
    ///
    ///
    /// ===== src/Config/app.json =====
    ///
    /// { "Logging": { ... } }
    /// </example>
    SingleMerged = 1,
}
