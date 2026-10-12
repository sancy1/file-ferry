// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/AppPage.cs
// PURPOSE:  The set of full-body pages the main window can host. The
//           harvest page is the three-panel workflow; the cloud page is
//           the cloud connection destination.
// RELATION: Used by MainWindowViewModel.CurrentPage and by the page-host
//           assignment in MainWindow.axaml.cs.
// ============================================================================

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The full-body pages the main window can host.
/// </summary>
public enum AppPage
{
    /// <summary>
    /// The three-panel harvest workflow. The default page.
    /// </summary>
    Harvest = 0,

    /// <summary>
    /// The cloud connection destination.
    /// </summary>
    Cloud = 1,

    /// <summary>
    /// Bulk path joiner workspace.
    /// </summary>
    LinkJoiner = 2,

    /// <summary>
    /// Local text extraction workspace.
    /// </summary>
    Extract = 3,

    /// <summary>
    /// Read-only database explorer workspace.
    /// </summary>
    SchemaLence = 4,
}
