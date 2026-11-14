// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/PromptConfirmDialogViewModel.cs
// PURPOSE:  The confirmation dialog's view model. A title, a message, and a
//           yes/no decision. The window owns completion; the view model only
//           carries the text. Used by the trash's Empty Trash confirmation
//           (and any other destructive prompt).
// RELATION: Created by PromptTrashDialog.axaml.cs's OnEmptyTrashClicked.
//           Bound by PromptConfirmDialog.axaml. The code-behind closes with
//           true (Yes) or false (No).
// ============================================================================

namespace FileHarvester.App.ViewModels;

/// <summary>View model for a simple yes/no confirmation.</summary>
public sealed class PromptConfirmDialogViewModel
{
    /// <summary>Creates the confirmation VM.</summary>
    public PromptConfirmDialogViewModel(string title, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Title = title;
        Message = message;
    }

    /// <summary>The window title.</summary>
    public string Title { get; }

    /// <summary>The message shown in the dialog.</summary>
    public string Message { get; }
}