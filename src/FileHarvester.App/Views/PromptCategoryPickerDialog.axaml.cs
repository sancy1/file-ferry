// ============================================================================
// FILE:     src/FileHarvester.App/Views/PromptCategoryPickerDialog.axaml.cs
// PURPOSE:  The category picker's code-behind. Closes on the VM's Saved
//           event (with the PromptCategoryPickerResult) or the Cancelled
//           event (with null).
// RELATION: Paired with PromptCategoryPickerDialog.axaml.
// ============================================================================

using Avalonia.Controls;
using FileHarvester.App.ViewModels;

namespace FileHarvester.App.Views;

/// <summary>The bulk-move category picker dialog.</summary>
public partial class PromptCategoryPickerDialog : Window
{
    /// <summary>Creates the dialog.</summary>
    public PromptCategoryPickerDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not PromptCategoryPickerViewModel vm) return;
        vm.Saved += OnSaved;
        vm.Cancelled += OnCancelled;
    }

    private void OnSaved(object? sender, PromptCategoryPickerResult result) => Close(result);

    private void OnCancelled(object? sender, EventArgs e) => Close(null);
}