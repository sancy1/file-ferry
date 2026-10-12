// ============================================================================
// FILE:     src\FileHarvester.App/ViewModels/OverwriteConfirmDialogViewModel.cs
// PURPOSE:  View model for the overwrite confirmation dialog. Shows the
//           target file name and carries the "apply to all remaining"
//           checkbox state. The window owns completion; this view model
//           only carries the state and produces the result.
// RELATION: Created by AvaloniaOverwriteConfirmer. Bound by
//           Views/OverwriteConfirmDialog.axaml. The dialog code-behind
//           calls CancelResult or ReplaceResult and closes with it.
// ============================================================================

using System.IO;
using FileHarvester.Core.Domain;

namespace FileHarvester.App.ViewModels;

/// <summary>View model for the overwrite confirmation dialog.</summary>
public sealed class OverwriteConfirmDialogViewModel : ViewModelBase
{
    private bool _applyToAllRemaining;

    public OverwriteConfirmDialogViewModel(string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        TargetPath = targetPath;
        FileName = Path.GetFileName(targetPath);
    }

    /// <summary>The absolute target path that already exists.</summary>
    public string TargetPath { get; }

    /// <summary>The display file name (the last segment of TargetPath).</summary>
    public string FileName { get; }

    /// <summary>
    /// Whether the user's choice applies to every remaining collision in
    /// the current harvest operation.
    /// </summary>
    public bool ApplyToAllRemaining
    {
        get => _applyToAllRemaining;
        set => SetProperty(ref _applyToAllRemaining, value);
    }

    /// <summary>
    /// The result when the user cancels: Cancel, with ApplyToAllRemaining
    /// forced to false. A cancel never applies to remaining files.
    /// </summary>
    public static OverwritePromptResult CancelResult() =>
        new(OverwriteDecision.Cancel, ApplyToAllRemaining: false);

    /// <summary>
    /// The result when the user replaces: Replace, carrying the current
    /// checkbox state.
    /// </summary>
    public OverwritePromptResult ReplaceResult() =>
        new(OverwriteDecision.Replace, ApplyToAllRemaining);
}
