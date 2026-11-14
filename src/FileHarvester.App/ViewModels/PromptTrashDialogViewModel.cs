// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/PromptTrashDialogViewModel.cs
// PURPOSE:  The trash dialog's view model. Loads the soft-deleted prompts
//           (SearchAsync with IncludeDeleted: true, filtered to IsDeleted),
//           restores a selected prompt, and empties the trash (HardDelete
//           per prompt). The confirmation for the Empty Trash is the code-
//           behind's PromptConfirmDialog; the VM exposes EmptyTrashAsync.
// RELATION: Constructed by PromptPage.axaml.cs when the Trash button is
//           clicked. Bound to PromptTrashDialog.axaml. Uses IPromptStore
//           (P1.3) and PromptListRowViewModel (the row wrapper).
// NOTES:    - The trash list is the soft-deleted prompts only; the
//             SearchAsync's IncludeDeleted: true returns all (including
//             active); the VM filters IsDeleted == true.
//           - RestoreAsync sets IsDeleted = 0; the prompt returns to the
//             list. HardDeleteAsync is the permanent delete; the code-behind
//             confirms before calling EmptyTrashAsync.
//           - The prompt rows are the PromptListRowViewModel; the list's
//             ItemTemplate binds Title/IsFavorite.
// ============================================================================

using System.Collections.ObjectModel;
using Prompt.Core.Domain;
using PromptRecord = Prompt.Core.Domain.Prompt;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The trash dialog's view model.
/// </summary>
public sealed class PromptTrashDialogViewModel : ViewModelBase
{
    private readonly IPromptStore _store;

    private PromptListRowViewModel? _selectedRow;
    private string? _statusText;
    private bool _isBusy;

    /// <summary>Creates the trash VM.</summary>
    public PromptTrashDialogViewModel(IPromptStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        RestoreCommand = new AsyncRelayCommand(RestoreAsync);
        CancelCommand = new RelayCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));
        _ = LoadAsync();
    }

    /// <summary>The soft-deleted prompts.</summary>
    public ObservableCollection<PromptListRowViewModel> TrashRows { get; } = new();

    /// <summary>The selected trash row, or null.</summary>
    public PromptListRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set => SetProperty(ref _selectedRow, value);
    }

    /// <summary>A short status message.</summary>
    public string? StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>True while loading or mutating.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>Restores the selected prompt.</summary>
    public AsyncRelayCommand RestoreCommand { get; }

    /// <summary>Closes the dialog.</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>Raised after a successful restore or empty; the page reloads the list.</summary>
    public event EventHandler? Saved;

    /// <summary>Raised when the dialog is cancelled.</summary>
    public event EventHandler? Cancelled;

    /// <summary>Loads the soft-deleted prompts into TrashRows.</summary>
    public async Task LoadAsync()
    {
        IsBusy = true;
        StatusText = "Loading trash...";
        try
        {
            var query = new PromptQuery(null, PromptScope.All, null, null, null, false, true, 0, 1000);
            var all = await _store.SearchAsync(query).ConfigureAwait(true);
            TrashRows.Clear();
            foreach (var p in all.Where(p => p.IsDeleted))
            {
                TrashRows.Add(new PromptListRowViewModel(p));
            }
            StatusText = TrashRows.Count == 0
                ? "The trash is empty."
                : TrashRows.Count + " prompt(s) in the trash.";
        }
        catch (Exception ex)
        {
            StatusText = "Could not load the trash: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RestoreAsync()
    {
        if (SelectedRow is null) { StatusText = "Select a prompt to restore."; return; }
        var id = SelectedRow.Prompt.Id;
        try
        {
            await _store.RestoreAsync(id).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            StatusText = "Restored.";
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusText = "Could not restore: " + ex.Message;
        }
    }

    /// <summary>Permanently deletes every prompt in the trash. The code-behind confirms first.</summary>
    public async Task EmptyTrashAsync()
    {
        if (TrashRows.Count == 0) { StatusText = "The trash is already empty."; return; }
        IsBusy = true;
        StatusText = "Emptying trash...";
        try
        {
            foreach (var row in TrashRows.ToList())
            {
                await _store.HardDeleteAsync(row.Prompt.Id).ConfigureAwait(true);
            }
            await LoadAsync().ConfigureAwait(true);
            StatusText = "Trash emptied.";
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusText = "Could not empty the trash: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}