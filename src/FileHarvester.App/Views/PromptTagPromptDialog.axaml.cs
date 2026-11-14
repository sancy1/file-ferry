// ============================================================================
// FILE:     src/FileHarvester.App/Views/PromptTagPromptDialog.axaml.cs
// PURPOSE:  The bulk add-tag prompt's code-behind. Closes on the VM's Saved
//           event (with the trimmed tag) or the Cancelled event (with null).
// RELATION: Paired with PromptTagPromptDialog.axaml.
// ============================================================================

using Avalonia.Controls;
using FileHarvester.App.ViewModels;

namespace FileHarvester.App.Views;

/// <summary>The bulk add-tag prompt dialog.</summary>
public partial class PromptTagPromptDialog : Window
{
    /// <summary>Creates the dialog.</summary>
    public PromptTagPromptDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not PromptTagPromptViewModel vm) return;
        vm.Saved += OnSaved;
        vm.Cancelled += OnCancelled;
    }

    private void OnSaved(object? sender, string tag) => Close(tag);

    private void OnCancelled(object? sender, EventArgs e) => Close(null);
}