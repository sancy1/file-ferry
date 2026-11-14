// ============================================================================
// FILE:     src/CodeViewer.Core/Domain/DisplayMode.cs
// PURPOSE:  The three CodeViewer presentations. All three consume the same
//           per-project selection engine; only the right-hand workspace
//           changes. The Project Browser remains visible in every mode.
// ============================================================================

namespace CodeViewer.Core.Domain;

/// <summary>
/// The active CodeViewer presentation.
/// </summary>
public enum DisplayMode
{
    /// <summary>
    /// The Project Tree Structure viewer occupies the entire right-hand
    /// workspace. The Code Content viewer is hidden.
    /// </summary>
    StructureOnly,

    /// <summary>
    /// The Code Content viewer occupies the entire right-hand workspace.
    /// The Project Tree Structure viewer is hidden.
    /// </summary>
    CodeOnly,

    /// <summary>
    /// Both viewers appear simultaneously as independent panes beside the
    /// Project Browser: structure above, code below.
    /// </summary>
    StructureAndCode,
}
