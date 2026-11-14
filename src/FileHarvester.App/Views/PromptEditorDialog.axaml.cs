// ============================================================================
// FILE:     src/FileHarvester.App/Views/PromptEditorDialog.axaml.cs
// PURPOSE:  The prompt editor dialog's code-behind. InitializeComponent, plus
//           the Saved/Cancelled event subscriptions that close the dialog.
// RELATION: Paired with PromptEditorDialog.axaml. Opened by
//           PromptPage.axaml.cs's NewPrompt / Edit handlers with the
//           PromptEditorViewModel as DataContext.
// NOTES:    - Saved (with the created/updated prompt) closes the dialog
//             successfully. Cancelled closes it without a result.
//           - The VM raises Saved/Cancelled; this code-behind is the only
//             subscriber. Close(result) / Close() are Avalonia Window APIs.
// ============================================================================

using Avalonia.Controls;
using FileHarvester.App.ViewModels;
using Prompt.Core.Domain;
using PromptRecord = Prompt.Core.Domain.Prompt;

namespace FileHarvester.App.Views;

/// <summary>
/// The prompt editor dialog. Closes on the VM's Saved or Cancelled event.
/// </summary>
public partial class PromptEditorDialog : Window
{
    /// <summary>Creates the dialog.</summary>
    public PromptEditorDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not PromptEditorViewModel vm) return;
        vm.Saved += OnSaved;
        vm.Cancelled += OnCancelled;
    }

    private void OnSaved(object? sender, PromptRecord result) => Close(result);

    private void OnCancelled(object? sender, EventArgs e) => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        (DataContext as PromptEditorViewModel)?.Dispose();
    }
}