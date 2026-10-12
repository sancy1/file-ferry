// ============================================================================
// FILE:     src\FileHarvester.App/Services/AvaloniaOverwriteConfirmer.cs
// PURPOSE:  The App-side IOverwriteConfirmer. Presents the modal
//           OverwriteConfirmDialog on the UI thread and returns the
//           user's decision.
// RELATION: Implements IOverwriteConfirmer. Registered in App.axaml.cs
//           before AddFileHarvester so it overrides the fail-closed
//           Core default (registered with TryAddSingleton).
// NOTES
//   The confirmer is not unit-testable offline: it opens a modal dialog
//   and requires a UI host. Its behavior is verified by manual
//   click-through. The Core fail-closed default and the sink's overwrite
//   guard are covered by the existing OutputSink tests.
//   Cancellation: if the token fires while the dialog is open, the
//   dialog closes with Cancel, ApplyToAllRemaining = false. The
//   confirmer never writes to the target path.
// ============================================================================

using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using FileHarvester.App.ViewModels;
using FileHarvester.App.Views;
using FileHarvester.Core.Domain;

namespace FileHarvester.App.Services;

internal sealed class AvaloniaOverwriteConfirmer : IOverwriteConfirmer
{
    public Task<OverwritePromptResult> ConfirmAsync(
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        if (!Dispatcher.UIThread.CheckAccess())
        {
            return Dispatcher.UIThread
                .InvokeAsync(() => ConfirmOnUiThreadAsync(targetPath, cancellationToken));
        }

        return ConfirmOnUiThreadAsync(targetPath, cancellationToken);
    }

    private static async Task<OverwritePromptResult> ConfirmOnUiThreadAsync(
        string targetPath,
        CancellationToken cancellationToken)
    {
        var owner = (Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        var vm = new OverwriteConfirmDialogViewModel(targetPath);
        var dialog = new OverwriteConfirmDialog { DataContext = vm };

        var cancelled = new OverwritePromptResult(
            OverwriteDecision.Cancel, ApplyToAllRemaining: false);

        using var reg = cancellationToken.Register(() =>
            Dispatcher.UIThread.Post(() => dialog.Close(cancelled)));

        if (owner is null)
        {
            return cancelled;
        }

        var result = await dialog
            .ShowDialog<OverwritePromptResult?>(owner)
            .ConfigureAwait(true);

        return result ?? cancelled;
    }
}
