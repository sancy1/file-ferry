// ============================================================================
// FILE:     src/FileHarvester.App/Views/PromptConfirmDialog.axaml.cs
// PURPOSE:  The confirmation dialog's code-behind. The Yes/No buttons close
//           the window with true/false (the ShowDialog<bool> result).
// RELATION: Paired with PromptConfirmDialog.axaml.
// ============================================================================

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FileHarvester.App.Views;

/// <summary>
/// A simple yes/no confirmation dialog.
/// </summary>
public partial class PromptConfirmDialog : Window
{
    /// <summary>Creates the dialog.</summary>
    public PromptConfirmDialog()
    {
        InitializeComponent();
    }

    private void OnYesClicked(object? sender, RoutedEventArgs e) => Close(true);

    private void OnNoClicked(object? sender, RoutedEventArgs e) => Close(false);
}