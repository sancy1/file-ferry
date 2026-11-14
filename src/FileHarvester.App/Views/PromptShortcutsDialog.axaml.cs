// ============================================================================
// FILE:     src/FileHarvester.App/Views/PromptShortcutsDialog.axaml.cs
// PURPOSE:  Code-behind for the keyboard shortcuts dialog.
// ============================================================================
using Avalonia.Controls;
using FileHarvester.App.ViewModels;
namespace FileHarvester.App.Views;

/// <summary>Code-behind for the keyboard shortcuts dialog.</summary>
public partial class PromptShortcutsDialog : Window
{
    /// <summary>Creates the dialog and wires the CloseRequested close.</summary>
    public PromptShortcutsDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is PromptShortcutsDialogViewModel vm)
        {
            vm.CloseRequested += (_, _) => Close();
        }
    }
}