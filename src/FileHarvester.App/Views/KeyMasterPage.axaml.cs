// ============================================================================
// filepath: src/FileHarvester.App/Views/KeyMasterPage.axaml.cs
// layer:    App / Views
// module:   FileHarvester.App.Views
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  Code-behind for the KeyMaster page. No logic; the page is fully
//           data-bound.
// ============================================================================

using Avalonia.Controls;

namespace FileHarvester.App.Views;

/// <summary>
/// The KeyMaster tool page.
/// </summary>
public partial class KeyMasterPage : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="KeyMasterPage"/> class.</summary>
    public KeyMasterPage()
    {
        InitializeComponent();
    }
}
