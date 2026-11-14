// ============================================================================
// FILE:     src/FileHarvester.App/Views/PromptListPanel.axaml.cs
// PURPOSE:  The sidebar panel's code-behind. InitializeComponent only. The
//           tree and list selections are bound through SelectedItem
//           two-way bindings to the view model; no code-behind event
//           handlers at P2.3.
// RELATION: Paired with PromptListPanel.axaml.
// NOTES:    No interactions here. The DataContext is the
//           PromptListViewModel provided by PromptPageViewModel (via the
//           PromptPage.axaml sidebar Border's DataContext binding).
// ============================================================================

using Avalonia.Controls;

namespace FileHarvester.App.Views;

/// <summary>
/// The sidebar panel: category tree and prompt list. Selections are
/// command-driven via the view model.
/// </summary>
public partial class PromptListPanel : UserControl
{
    /// <summary>Creates the panel.</summary>
    public PromptListPanel()
    {
        InitializeComponent();
    }
}