// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/PromptShortcutsDialogViewModel.cs
// PURPOSE:  Read-only view model for the keyboard shortcuts dialog.
// ============================================================================
using System.Windows.Input;
namespace FileHarvester.App.ViewModels;

/// <summary>Read-only view model for the keyboard shortcuts dialog.</summary>
public sealed class PromptShortcutsDialogViewModel : ViewModelBase
{
    /// <summary>Raised by CloseCommand when the user dismisses the dialog.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Creates the dialog VM; the rows are static.</summary>
    public PromptShortcutsDialogViewModel()
    {
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>The shortcut rows shown in the dialog, in presentation order.</summary>
    public IReadOnlyList<ShortcutRow> Rows { get; } = new[]
    {
        new ShortcutRow("Ctrl+N", "New prompt"),
        new ShortcutRow("Ctrl+F", "Focus the search box"),
        new ShortcutRow("Ctrl+E", "Export the selected prompt"),
        new ShortcutRow("Delete", "Move the selected prompt to Trash"),
        new ShortcutRow("Ctrl+S", "Save the prompt being edited"),
        new ShortcutRow("Ctrl+Z / Ctrl+Y", "Undo / redo in the editor body"),
    };

    /// <summary>Dismisses the dialog.</summary>
    public ICommand CloseCommand { get; }

    /// <summary>One row: the key combination and what it does.</summary>
    public sealed record ShortcutRow(string Keys, string Description);
}