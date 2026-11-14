// ============================================================================
// FILE:     src/FileHarvester.App/Views/CategoryManagerDialog.axaml.cs
// PURPOSE:  The category manager dialog's code-behind. InitializeComponent,
//           plus the Closed event subscription that closes the dialog.
// RELATION: Paired with CategoryManagerDialog.axaml. Opened by
//           PromptPage.axaml.cs when the sidebar's Manage button is clicked.
// NOTES:    - The VM raises Closed; this code-behind is the only subscriber.
//           - The VM raises Changed on every mutation; the page reloads the
//             sidebar via that event (the page's handler).
// ============================================================================

using Avalonia.Controls;
using FileHarvester.App.ViewModels;

namespace FileHarvester.App.Views;

/// <summary>
/// The category manager dialog. Closes on the VM's Closed event.
/// </summary>
public partial class CategoryManagerDialog : Window
{
    /// <summary>Creates the dialog.</summary>
    public CategoryManagerDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not CategoryManagerViewModel vm) return;
        vm.Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e) => Close();
}