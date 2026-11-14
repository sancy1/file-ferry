// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/PromptViewerViewModel.cs
// PURPOSE:  The viewer's view model. At P2.4 it holds the selected prompt,
//           exposes the display fields (title, body, category label,
//           favorite state, has-selection), and the toolbar's commands
//           (Copy, Edit, Delete, Favorite). Export is deferred to P3.4.
// RELATION: Constructed by PromptPageViewModel (P2.4) with IPromptStore.
//           PromptListViewModel.PromptSelected calls SetPrompt. The
//           PromptPage.axaml's viewer area binds this VM via Prompt.Viewer.
// NOTES:    - CopyCommand raises CopyRequested with the selected body; the
//             PromptPage.axaml.cs handles the clipboard access via
//             TopLevel.GetTopLevel. The VM stays UI-free.
//           - EditCommand is a no-op at P2.4 (the editor arrives at P2.5).
//             The button is disabled with a tooltip.
//           - DeleteCommand calls SoftDeleteAsync, then clears the viewer.
//           - FavoriteCommand toggles via PromptPatch with IsFavorite
//             specified; every other field is Unspecified (P1.3 semantics).
//           - GetCategoryLabel is provided by the list VM (the tree already
//             has the category names; no additional store call).
// ============================================================================

using Prompt.Core.Domain;
using PromptRecord = Prompt.Core.Domain.Prompt;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The viewer's view model. Holds the selected prompt and the display
/// fields and commands.
/// </summary>
public sealed class PromptViewerViewModel : ViewModelBase
{
    private readonly IPromptStore _prompts;
    private PromptRecord? _selectedPrompt;
    private string? _selectedCategoryLabel;
    private bool _isFavorite;

    /// <summary>
    /// Creates the viewer VM. The prompt store is used by Delete and Favorite.
    /// </summary>
    /// <param name="prompts">The prompt store. Never null.</param>
    public PromptViewerViewModel(IPromptStore prompts)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        _prompts = prompts;
        CopyCommand = new RelayCommand(OnCopy);
        EditCommand = new RelayCommand(OnEdit);
        ExportCommand = new AsyncRelayCommand(OnExportAsync);
        HistoryCommand = new AsyncRelayCommand(OnHistoryAsync);
        DeleteCommand = new AsyncRelayCommand(OnDeleteAsync);
        FavoriteCommand = new AsyncRelayCommand(OnFavoriteAsync);
        ShortcutsCommand = new RelayCommand(() => ShortcutsRequested?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>The currently selected prompt, or null.</summary>
    public PromptRecord? SelectedPrompt
    {
        get => _selectedPrompt;
        private set => SetProperty(ref _selectedPrompt, value);
    }

    /// <summary>True when a prompt is selected.</summary>
    public bool HasSelection => _selectedPrompt is not null;

    /// <summary>The selected prompt's title, or null.</summary>
    public string? SelectedTitle => _selectedPrompt?.Title;

    /// <summary>The selected prompt's body, or null.</summary>
    public string? SelectedBody => _selectedPrompt?.Body;

    /// <summary>The breadcrumb: "Category / SubCategory" or "Uncategorized".</summary>
    public string? SelectedCategoryLabel
    {
        get => _selectedCategoryLabel;
        private set => SetProperty(ref _selectedCategoryLabel, value);
    }

    /// <summary>True when the selected prompt is favorited.</summary>
    public bool IsFavorite
    {
        get => _isFavorite;
        private set => SetProperty(ref _isFavorite, value);
    }

    /// <summary>
    /// Raised by <see cref="CopyCommand"/> with the body to copy. The page
    /// code-behind handles the clipboard access.
    /// </summary>
    public event EventHandler<string>? CopyRequested;
    
    /// <summary>Raised when Edit is clicked with a selected prompt.</summary>
    public event EventHandler<PromptRecord>? EditRequested;

    /// <summary>Raised when Export is clicked with a selected prompt.</summary>
    public event EventHandler<PromptRecord>? ExportRequested;

    /// <summary>Raised when History is clicked with a selected prompt.</summary>
    public event EventHandler<PromptRecord>? HistoryRequested;

    /// <summary>Raised when a soft-delete succeeds; the page reloads the list.</summary>
    public event EventHandler? DeleteSucceeded;

    /// <summary>Raised when the Shortcuts toolbar button is clicked; the page opens the shortcuts dialog.</summary>
    public event EventHandler? ShortcutsRequested;

    /// <summary>Raises <see cref="ShortcutsRequested"/>. Bound by the toolbar's "?" button.</summary>
    public RelayCommand ShortcutsCommand { get; }



    /// <summary>Copies the selected body. Raised via CopyRequested.</summary>
    public RelayCommand CopyCommand { get; }

    /// <summary>Reserved for P2.5 (the editor). No-op at P2.4.</summary>
    public RelayCommand EditCommand { get; }

    /// <summary>Raises ExportRequested with the selected prompt.</summary>
    public AsyncRelayCommand ExportCommand { get; }

    /// <summary>Raises HistoryRequested with the selected prompt.</summary>
    public AsyncRelayCommand HistoryCommand { get; }

    /// <summary>Soft-deletes the selected prompt, then clears the viewer.</summary>
    public AsyncRelayCommand DeleteCommand { get; }

    /// <summary>Toggles the selected prompt's favorite flag.</summary>
    public AsyncRelayCommand FavoriteCommand { get; }

    /// <summary>
    /// Selects the given prompt. Called by the list VM's PromptSelected event.
    /// </summary>
    /// <param name="prompt">The selected prompt. Never null.</param>
    /// <param name="list">The list VM, for the category label lookup.</param>
    public void SetPrompt(PromptRecord prompt, PromptListViewModel list)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(list);
        SelectedPrompt = prompt;
        SelectedCategoryLabel = list.GetCategoryLabel(prompt.CategoryId, prompt.SubCategoryId);
        IsFavorite = prompt.IsFavorite;
        list.StatusText = "Selected: " + prompt.Title;
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedBody));
    }

    /// <summary>Clears the viewer to the empty state.</summary>
    public void Clear()
    {
        SelectedPrompt = null;
        SelectedCategoryLabel = null;
        IsFavorite = false;
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedBody));
    }

    private void OnCopy()
    {
        if (SelectedBody is null) return;
        CopyRequested?.Invoke(this, SelectedBody);
    }

    private void OnEdit()
    {
        if (SelectedPrompt is null) return;
        EditRequested?.Invoke(this, SelectedPrompt);
    }

    private Task OnExportAsync()
    {
        if (SelectedPrompt is null) return Task.CompletedTask;
        ExportRequested?.Invoke(this, SelectedPrompt);
        return Task.CompletedTask;
    }

    private Task OnHistoryAsync()
    {
        if (SelectedPrompt is null) return Task.CompletedTask;
        HistoryRequested?.Invoke(this, SelectedPrompt);
        return Task.CompletedTask;
    }

    private async Task OnDeleteAsync()
    {
        if (SelectedPrompt is null) return;
        await _prompts.SoftDeleteAsync(SelectedPrompt.Id).ConfigureAwait(true);
        Clear();
        DeleteSucceeded?.Invoke(this, EventArgs.Empty);
    }

    private async Task OnFavoriteAsync()
    {
        if (SelectedPrompt is null) return;
        var newValue = !IsFavorite;
        var patch = new PromptPatch(
            new PatchField<string>(false, default),
            new PatchField<string>(false, default),
            new PatchField<BodyFormat>(false, default),
            new PatchField<Guid?>(false, default),
            new PatchField<Guid?>(false, default),
            new PatchField<string?>(false, null),
            new PatchField<bool>(true, newValue),
            new PatchField<IReadOnlyList<string>>(false, null));
        await _prompts.PatchAsync(SelectedPrompt.Id, patch).ConfigureAwait(true);
        IsFavorite = newValue;
    }
}