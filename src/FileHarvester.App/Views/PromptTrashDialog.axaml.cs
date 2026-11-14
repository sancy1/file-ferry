// ============================================================================
// FILE:     src/FileHarvester.App/Views/PromptTrashDialog.axaml.cs
// PURPOSE:  The trash dialog's code-behind. Closes on Saved/Cancelled.
//           Handles the Empty Trash button by showing the PromptConfirmDialog
//           (the destructive confirmation) and, on Yes, calling the VM's
//           EmptyTrashAsync.
// RELATION: Paired with PromptTrashDialog.axaml. Opened by PromptPage.axaml.cs
//           when the Trash button is clicked.
// NOTES:    - The Empty Trash is destructive (HardDeleteAsync); the
//             PromptConfirmDialog is the explicit confirmation. The Cancel
//             path never calls EmptyTrashAsync.
//           - The Saved event is raised by the VM after the Restore/Empty;
//             the dialog closes; the page reloads the list.
// ============================================================================

using Avalonia.Controls;
using Avalonia.Interactivity;
using FileHarvester.App.ViewModels;

namespace FileHarvester.App.Views;

/// <summary>
/// The trash dialog.
/// </summary>
public partial class PromptTrashDialog : Window
{
    /// <summary>Creates the dialog.</summary>
    public PromptTrashDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private PromptTrashDialogViewModel? ViewModel => DataContext as PromptTrashDialogViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not PromptTrashDialogViewModel vm) return;
        vm.Saved += OnSaved;
        vm.Cancelled += OnCancelled;
    }

    private void OnSaved(object? sender, EventArgs e) => Close(true);

    private void OnCancelled(object? sender, EventArgs e) => Close(false);

    private async void OnEmptyTrashClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        var owner = this;
        var confirm = new PromptConfirmDialog
        {
            DataContext = new PromptConfirmDialogViewModel(
                "Empty the trash?",
                "This permanently deletes every prompt in the trash. It cannot be undone."),
        };
        var yes = await confirm.ShowDialog<bool>(owner);
        if (yes)
        {
            await ViewModel.EmptyTrashAsync();
        }
    }
}