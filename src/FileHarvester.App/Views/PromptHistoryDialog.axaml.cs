// ============================================================================
// FILE:     src/FileHarvester.App/Views/PromptHistoryDialog.axaml.cs
// PURPOSE:  The version history dialog's code-behind. Closes on the VM's
//           Saved event (true) or Cancelled event (false).
// RELATION: Paired with PromptHistoryDialog.axaml. Opened by
//           PromptPage.axaml.cs when the viewer's History button is clicked.
// ============================================================================

using Avalonia.Controls;
using Avalonia.Interactivity;
using FileHarvester.App.ViewModels;

namespace FileHarvester.App.Views;

/// <summary>
/// The version history dialog.
/// </summary>
public partial class PromptHistoryDialog : Window
{
    /// <summary>Creates the dialog.</summary>
    public PromptHistoryDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not PromptHistoryDialogViewModel vm) return;
        vm.Saved += OnSaved;
        vm.Cancelled += OnCancelled;
    }

    private void OnSaved(object? sender, EventArgs e) => Close(true);

    private void OnCancelled(object? sender, EventArgs e) => Close(false);

    private async void OnClearHistoryClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PromptHistoryDialogViewModel vm) return;
        var confirm = new PromptConfirmDialog
        {
            DataContext = new PromptConfirmDialogViewModel(
                "Clear the version history?",
                "This permanently deletes every saved version of this prompt. The current body is unchanged; the next edit creates the first new snapshot."),
        };
        var yes = await confirm.ShowDialog<bool>(this);
        if (yes)
        {
            await vm.ClearHistoryAsync();
        }
    }
}