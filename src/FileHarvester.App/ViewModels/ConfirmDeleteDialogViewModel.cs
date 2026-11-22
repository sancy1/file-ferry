// ============================================================================
// filepath: src/FileHarvester.App/ViewModels/ConfirmDeleteDialogViewModel.cs
// layer:    App / ViewModels
// module:   FileHarvester.App.ViewModels
// since:    Phase A (Deployment Vault registry)
// purpose:  View model for the permanent-delete confirmation dialog. Holds
//           the dialog title and message. The dialog itself returns a bool
//           (true = delete, false = cancel); this VM has no command.
//
// RELATIONSHIPS
//   Implements : FileHarvester.App.ViewModels.ViewModelBase
//   Depends on : n/a (BCL only)
//   Used by    : ConfirmDeleteDialog.axaml, MainWindowViewModel.ConfirmDeletionAsync
//   See also   : HarvestResultDialogViewModel (pattern reference),
//                AmbiguityDialog.axaml.cs (result-returning close pattern)
//
// NOTES
//   - Read-only. No commands. The dialog's buttons close the window with
//     Close(true) / Close(false); the caller awaits ShowDialog<bool>.
//   - Fail-closed: closing the dialog with the X button returns false
//     (default bool), so the delete is cancelled.
// ============================================================================

namespace FileHarvester.App.ViewModels;

/// <summary>
/// View model for the permanent-delete confirmation dialog.
/// </summary>
public sealed class ConfirmDeleteDialogViewModel : ViewModelBase
{
    /// <summary>Builds the dialog view model.</summary>
    /// <param name="title">The dialog title. Not null or whitespace.</param>
    /// <param name="message">The confirmation message. Not null or whitespace.</param>
    /// <exception cref="ArgumentException">
    /// When <paramref name="title"/> or <paramref name="message"/> is null,
    /// empty, or whitespace.
    /// </exception>
    public ConfirmDeleteDialogViewModel(string title, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Title = title;
        Message = message;
    }

    /// <summary>The dialog title.</summary>
    public string Title { get; }

    /// <summary>The confirmation message shown to the user.</summary>
    public string Message { get; }
}