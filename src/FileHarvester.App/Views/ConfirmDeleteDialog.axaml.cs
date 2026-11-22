// ============================================================================
// filepath: src/FileHarvester.App/Views/ConfirmDeleteDialog.axaml.cs
// layer:    App / Views
// module:   FileHarvester.App.Views
// since:    Phase A (Deployment Vault registry)
// purpose:  Code-behind for the permanent-delete confirmation dialog. Wires
//           the Delete and Cancel buttons to Close(true) / Close(false).
//
// RELATIONSHIPS
//   Implements : n/a (Avalonia Window code-behind)
//   Depends on : Avalonia.Controls.Window, Avalonia.Interactivity,
//                Avalonia.Markup.Xaml.AvaloniaXamlLoader,
//                FileHarvester.App.ViewModels.ConfirmDeleteDialogViewModel
//   Used by    : MainWindowViewModel.ConfirmDeletionAsync via ShowDialog<bool>
//   See also   : AmbiguityDialog.axaml.cs (the same Close(result) pattern)
//
// NOTES
//   - Closing via the window X returns default(bool) = false. That is the
//     correct fail-closed behavior for a permanent delete.
//   - The dialog has no VM commands; the buttons close the window directly.
// ============================================================================

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace FileHarvester.App.Views;

/// <summary>
/// Code-behind for the permanent-delete confirmation dialog.
/// </summary>
public partial class ConfirmDeleteDialog : Window
{
    /// <summary>Initializes the dialog and wires the buttons.</summary>
    public ConfirmDeleteDialog()
    {
        InitializeComponent();
        WireButtons();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void WireButtons()
    {
        Wire("DeleteButton", (_, _) => Close(true));
        Wire("CancelButton", (_, _) => Close(false));
    }

    private void Wire(string name, System.EventHandler<RoutedEventArgs> handler)
    {
        var button = this.FindControl<Button>(name);
        if (button is not null)
        {
            button.Click += handler;
        }
    }
}