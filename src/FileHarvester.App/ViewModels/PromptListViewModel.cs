// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/PromptListViewModel.cs
// PURPOSE:  The sidebar's view model. Holds the category tree and the
//           prompt list, drives the tree's selection filter, raises the
//           PromptSelected event when a prompt is selected.
// RELATION: Constructed by PromptPageViewModel (P2.3). Bound to
//           PromptListPanel.axaml. Uses ICategoryStore, ISubCategoryStore,
//           and IPromptStore (P1.2, P1.3). The stores' operations are
//           already wrapped in the prompt-store policy - this VM does not
//           add retries.
// NOTES:    - LoadAsync builds the tree (categories with sub-category
//             children, plus the display-only Uncategorized pseudo-node
//             when prompts exist with CategoryId == null) and loads all
//             prompts. The list is filtered on tree selection.
//           - P2.8: SearchText, SearchTags, FavoritesOnly, SearchScope are
//             the search bar's four properties. StartDebounce cancels the
//             previous 250 ms debounce via a CancellationTokenSource and
//             reloads on elapse. BuildQuery merges the four into the
//             PromptQuery. SearchTags is comma-separated in P2.8; the
//             multi-select UI is a P2.8.x refinement.
//           - The prompt list is bounded at 100 items (P1.5 default page
//             size). Pagination and virtualization arrive at P4.x.
//           - The PromptSelected event is subscribed by PromptPageViewModel;
//             the handler calls PromptViewerViewModel.SetPrompt.
// ============================================================================

using System.Collections.ObjectModel;
using System.Windows.Input;
using Prompt.Core.Domain;
using PromptRecord = Prompt.Core.Domain.Prompt;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The sidebar: categories tree and prompt list, filtered by the selected
/// tree node.
/// </summary>
public sealed class PromptListViewModel : ViewModelBase, IDisposable
{
    private readonly ICategoryStore _categories;
    private readonly ISubCategoryStore _subCategories;
    private readonly IPromptStore _prompts;

    private PromptCategoryRowViewModel? _selectedCategory;
    private PromptListRowViewModel? _selectedPrompt;
    private readonly ObservableCollection<PromptListRowViewModel> _selectedPrompts = new();
    private string? _statusText;
    private string _prospectsHeader = "ALL PROMPTS";
    private string _searchText = string.Empty;
    private string _searchTags = string.Empty;
    private bool _favoritesOnly;
    private string _searchScope = "All";
    private CancellationTokenSource? _debounceCts;

    /// <summary>
    /// Creates the sidebar VM. LoadAsync is not called in the constructor;
    /// the caller (PromptPageViewModel) calls it fire-and-forget.
    /// </summary>
    public PromptListViewModel(
        ICategoryStore categories,
        ISubCategoryStore subCategories,
        IPromptStore prompts)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(subCategories);
        ArgumentNullException.ThrowIfNull(prompts);
        _categories = categories;
        _subCategories = subCategories;
        _prompts = prompts;

        ShowAllCommand = new RelayCommand(() =>
        {
            // F-270: full reset - the search filters and the tree selection
            // are cleared together. The three search setters fire the
            // debounce (the last's debounce wins); the SelectedCategory
            // setter's reload fires immediately. After the debounce elapses,
            // the reload runs with the full state - the full list returns.
            SearchText = string.Empty;
            SearchTags = string.Empty;
            FavoritesOnly = false;
            SelectedCategory = null;
        });
        ManageCategoriesCommand = new RelayCommand(() => ManageCategoriesRequested?.Invoke(this, EventArgs.Empty));
        BulkExportCommand = new RelayCommand(() => BulkExportRequested?.Invoke(this, EventArgs.Empty), () => HasSelection);
        BulkDeleteCommand = new RelayCommand(() => BulkDeleteRequested?.Invoke(this, EventArgs.Empty), () => HasSelection);
        BulkMoveCommand = new RelayCommand(() => BulkMoveRequested?.Invoke(this, EventArgs.Empty), () => HasSelection);
        BulkAddTagCommand = new RelayCommand(() => BulkAddTagRequested?.Invoke(this, EventArgs.Empty), () => HasSelection);
        _selectedPrompts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSelection));
            ((RelayCommand)BulkExportCommand).RaiseCanExecuteChanged();
            ((RelayCommand)BulkDeleteCommand).RaiseCanExecuteChanged();
            ((RelayCommand)BulkMoveCommand).RaiseCanExecuteChanged();
            ((RelayCommand)BulkAddTagCommand).RaiseCanExecuteChanged();
        };
    }

    /// <summary>The category tree (categories with sub-category children).</summary>
    public ObservableCollection<PromptCategoryRowViewModel> Categories { get; } = new();

    /// <summary>The prompts under the current tree selection.</summary>
    public ObservableCollection<PromptListRowViewModel> VisiblePrompts { get; } = new();

    /// <summary>The currently selected tree node, or null.</summary>
    public PromptCategoryRowViewModel? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                OnPropertyChanged(nameof(ProspectsHeader));
                _ = ReloadPromptsAsync();
            }
        }
    }

    /// <summary>The currently selected prompt, or null.</summary>
    public PromptListRowViewModel? SelectedPrompt
    {
        get => _selectedPrompt;
        set
        {
            if (SetProperty(ref _selectedPrompt, value))
            {
                if (value is not null)
                {
                    PromptSelected?.Invoke(this, value.Prompt);
                }
            }
        }
    }

    /// <summary>A short status message. Set by the load path and the viewer stub.</summary>
    public string? StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>The search bar's text. Empty = no text filter.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                StartDebounce();
            }
        }
    }

    /// <summary>The search bar's tags (comma-separated). Empty = no tag filter.</summary>
    public string SearchTags
    {
        get => _searchTags;
        set
        {
            if (SetProperty(ref _searchTags, value))
            {
                StartDebounce();
            }
        }
    }

    /// <summary>True = show only favorited prompts.</summary>
    public bool FavoritesOnly
    {
        get => _favoritesOnly;
        set
        {
            if (SetProperty(ref _favoritesOnly, value))
            {
                StartDebounce();
            }
        }
    }

    /// <summary>The scope dropdown's items. The ComboBox binds ItemsSource to this.</summary>
    public IReadOnlyList<string> ScopeOptions { get; } = new[] { "All", "Current Category", "Current Sub-Category" };

    /// <summary>The search bar's scope: "All", "Current Category", "Current Sub-Category".</summary>
    public string SearchScope
    {
        get => _searchScope;
        set
        {
            if (SetProperty(ref _searchScope, value))
            {
                StartDebounce();
            }
        }
    }

    /// <summary>The list header: "ALL PROMPTS" or "PROMPTS IN "Name"".</summary>
    public string ProspectsHeader
    {
        get => _prospectsHeader;
        private set => SetProperty(ref _prospectsHeader, value);
    }

    /// <summary>
    /// Raised when the selected prompt changes. Carries the Prompt record.
    /// Subscribed by PromptPageViewModel to call the viewer.
    /// </summary>
    public event EventHandler<PromptRecord>? PromptSelected;

    /// <summary>The multi-selected prompts (the bulk actions' selection).</summary>
    public ObservableCollection<PromptListRowViewModel> SelectedPrompts => _selectedPrompts;

    /// <summary>True when the multi-select has at least one row.</summary>
    public bool HasSelection => _selectedPrompts.Count > 0;

    /// <summary>Raised when the bulk export is clicked. The page opens the export dialog for the many.</summary>
    public event EventHandler? BulkExportRequested;

    /// <summary>Raised when the bulk delete is clicked. The page confirms and calls BulkDeleteAsync.</summary>
    public event EventHandler? BulkDeleteRequested;

    /// <summary>Raised when the bulk move is clicked. The page shows the category picker.</summary>
    public event EventHandler? BulkMoveRequested;

    /// <summary>Raised when the bulk add-tag is clicked. The page shows the tag prompt.</summary>
    public event EventHandler? BulkAddTagRequested;

    /// <summary>Asks the page for the bulk export.</summary>
    public ICommand BulkExportCommand { get; }

    /// <summary>Asks the page for the bulk delete (with the confirmation).</summary>
    public ICommand BulkDeleteCommand { get; }

    /// <summary>Asks the page for the bulk move (the category picker).</summary>
    public ICommand BulkMoveCommand { get; }

    /// <summary>Asks the page for the bulk add-tag (the tag prompt).</summary>
    public ICommand BulkAddTagCommand { get; }

    /// <summary>
    /// Clears the tree selection, restoring the all-prompts view.
    /// </summary>
    public ICommand ShowAllCommand { get; }

    /// <summary>Raised when Manage is clicked. The page opens the category manager.</summary>
    public event EventHandler? ManageCategoriesRequested;

    /// <summary>Opens the category manager. The page handles the event.</summary>
    public ICommand ManageCategoriesCommand { get; }

    /// <summary>
    /// Loads the categories tree and the initial prompt list. Catches and
    /// surfaces errors via StatusText (does not throw).
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            StatusText = "Loading categories and prompts…";
            await BuildCategoryTreeAsync().ConfigureAwait(true);
            await ReloadPromptsAsync().ConfigureAwait(true);
            StatusText = Categories.Count == 0
                ? "No categories yet. The category manager arrives at P2.7."
                : $"{Categories.Count} categories, {VisiblePrompts.Count} prompts loaded.";
        }
        catch (Exception ex)
        {
            StatusText = "Could not load: " + ex.Message;
        }
    }
    /// <summary>
    /// Starts a 250 ms debounce. Cancels the previous; on elapse calls
    /// ReloadPromptsAsync. The CancellationTokenSource is the deterministic
    /// debounce - not a Task.Delay per keystroke, which would not cancel.
    /// </summary>
    private void StartDebounce()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        var ct = _debounceCts.Token;
        _ = DebouncedReloadAsync(ct);
    }

    private async Task DebouncedReloadAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(250, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (ct.IsCancellationRequested) return;
        await ReloadPromptsAsync().ConfigureAwait(true);
    }

    private async Task BuildCategoryTreeAsync()
    {
        Categories.Clear();

        var cats = await _categories.ListAsync().ConfigureAwait(true);
        var allPrompts = await _prompts.SearchAsync(
            new PromptQuery(null, PromptScope.All, null, null, null, false, false, 0, 1000))
            .ConfigureAwait(true);

        var promptsByCategory = allPrompts
            .Where(p => p.CategoryId.HasValue && !p.SubCategoryId.HasValue)
            .GroupBy(p => p.CategoryId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var promptsBySubCategory = allPrompts
            .Where(p => p.SubCategoryId.HasValue)
            .GroupBy(p => p.SubCategoryId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var cat in cats)
        {
            var subs = await _subCategories.ListAsync(cat.Id).ConfigureAwait(true);

            var subRows = subs
                .Select(s => new PromptCategoryRowViewModel(
                    s,
                    promptsBySubCategory.TryGetValue(s.Id, out var sp) ? sp.Count : 0))
                .ToList();

            var directCount = promptsByCategory.TryGetValue(cat.Id, out var cp) ? cp.Count : 0;
            var total = directCount + subRows.Sum(r => r.PromptCount);

            Categories.Add(new PromptCategoryRowViewModel(cat, subRows, total));
        }

        var uncategorizedCount = allPrompts.Count(p => !p.CategoryId.HasValue);
        if (uncategorizedCount > 0)
        {
            Categories.Add(PromptCategoryRowViewModel.Uncategorized(uncategorizedCount));
        }
    }

    private async Task ReloadPromptsAsync()
    {
        try
        {
            VisiblePrompts.Clear();
            var query = BuildQuery();
            var prompts = await _prompts.SearchAsync(query).ConfigureAwait(true);
            if (SelectedCategory?.IsPseudo == true)
            {
                prompts = prompts.Where(p => !p.CategoryId.HasValue).ToList();
            }
            foreach (var p in prompts)
            {
                VisiblePrompts.Add(new PromptListRowViewModel(p));
            }
            UpdateProspectsHeader();
        }
        catch (Exception ex)
        {
            StatusText = "Could not load prompts: " + ex.Message;
        }
    }

    private PromptQuery BuildQuery()
    {
        var text = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
        var tags = ParseTags(SearchTags);
        var fav = FavoritesOnly;

        // F-264: the search bar's SearchScope decides which of the tree's
        // IDs the query uses. "All" overrides the tree selection (no
        // category filter); "Current Category" uses the tree's category
        // (null if the tree is on the Uncategorized pseudo-node or a
        // sub-category - the sub-category case falls back to its parent
        // category); "Current Sub-Category" uses the tree's sub-category
        // (null if the tree is on a category - falls back to the category
        // scope). Unknown strings fall back to the tree-driven behavior.
        var (scope, categoryId, subCategoryId, take) = SearchScope switch
        {
            "Current Category" when SelectedCategory?.Category is not null =>
                (PromptScope.Category, (Guid?)SelectedCategory.Category.Id, (Guid?)null, 100),
            "Current Sub-Category" when SelectedCategory?.SubCategory is not null =>
                (PromptScope.SubCategory, (Guid?)null, (Guid?)SelectedCategory.SubCategory.Id, 100),
            "All" => (PromptScope.All, (Guid?)null, (Guid?)null, 100),
            _ => SelectedCategory switch
            {
                null => (PromptScope.All, (Guid?)null, (Guid?)null, 100),
                { IsPseudo: true } => (PromptScope.All, (Guid?)null, (Guid?)null, 1000),
                { SubCategory: not null } =>
                    (PromptScope.SubCategory, (Guid?)null, (Guid?)SelectedCategory.SubCategory.Id, 100),
                { Category: not null } =>
                    (PromptScope.Category, (Guid?)SelectedCategory.Category.Id, (Guid?)null, 100),
                _ => (PromptScope.All, (Guid?)null, (Guid?)null, 100),
            },
        };

        return new PromptQuery(text, scope, categoryId, subCategoryId, tags, fav, false, 0, take);
    }

    private static List<string>? ParseTags(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return parts.Count == 0 ? null : parts;
    }

    private void UpdateProspectsHeader()
    {
        if (SelectedCategory is null)
        {
            ProspectsHeader = "ALL PROMPTS";
        }
        else
        {
            var name = SelectedCategory.Category?.Name
                ?? SelectedCategory.SubCategory?.Name
                ?? "Uncategorized";
            ProspectsHeader = "PROMPTS IN \"" + name + "\"";
        }
    }

    /// <summary>
    /// The breadcrumb label for the given category/sub-category IDs:
    /// "Category / SubCategory", "Category", or "Uncategorized".
    /// </summary>
    public string GetCategoryLabel(Guid? categoryId, Guid? subCategoryId)
    {
        if (!categoryId.HasValue) return "Uncategorized";
        var cat = Categories.FirstOrDefault(c => c.Category?.Id == categoryId);
        if (cat is null) return "Unknown category";
        if (!subCategoryId.HasValue) return cat.Category!.Name;
        var sub = cat.Children.FirstOrDefault(s => s.SubCategory?.Id == subCategoryId);
        return sub is null ? cat.Category!.Name : cat.Category!.Name + " / " + sub.SubCategory!.Name;
    }

    /// <summary>
    /// Cancels and disposes the debounce CancellationTokenSource. Called by
    /// the owner (the page) when the page is disposed. No other resource is
    /// owned by this VM; the stores are injected singletons, not owned.
    /// </summary>
    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>Soft-deletes every selected prompt.</summary>
    public async Task BulkDeleteAsync(CancellationToken cancellationToken = default)
    {
        var ids = _selectedPrompts.Select(r => r.Prompt.Id).ToList();
        if (ids.Count == 0) return;
        try
        {
            foreach (var id in ids)
            {
                await _prompts.SoftDeleteAsync(id, cancellationToken).ConfigureAwait(true);
            }
            _selectedPrompts.Clear();
            await LoadAsync().ConfigureAwait(true);
            StatusText = "Deleted " + ids.Count + " prompt(s).";
        }
        catch (Exception ex)
        {
            StatusText = "Bulk delete failed: " + ex.Message;
        }
    }

    /// <summary>Moves every selected prompt to the category/sub-category.</summary>
    public async Task BulkMoveAsync(Guid? categoryId, Guid? subCategoryId, CancellationToken cancellationToken = default)
    {
        var ids = _selectedPrompts.Select(r => r.Prompt.Id).ToList();
        if (ids.Count == 0) return;
        try
        {
            foreach (var id in ids)
            {
                var patch = new PromptPatch(
                    new PatchField<string>(false, default),
                    new PatchField<string>(false, default),
                    new PatchField<BodyFormat>(false, default),
                    new PatchField<Guid?>(true, categoryId),
                    new PatchField<Guid?>(true, subCategoryId),
                    new PatchField<string?>(false, null),
                    new PatchField<bool>(false, default),
                    new PatchField<IReadOnlyList<string>>(false, null));
                await _prompts.PatchAsync(id, patch, cancellationToken).ConfigureAwait(true);
            }
            _selectedPrompts.Clear();
            await LoadAsync().ConfigureAwait(true);
            StatusText = "Moved " + ids.Count + " prompt(s).";
        }
        catch (Exception ex)
        {
            StatusText = "Bulk move failed: " + ex.Message;
        }
    }

    /// <summary>Attaches the tag to every selected prompt.</summary>
    public async Task BulkAddTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;
        var ids = _selectedPrompts.Select(r => r.Prompt.Id).ToList();
        if (ids.Count == 0) return;
        try
        {
            foreach (var id in ids)
            {
                await _prompts.AttachTagAsync(id, tag.Trim(), cancellationToken).ConfigureAwait(true);
            }
            _selectedPrompts.Clear();
            await LoadAsync().ConfigureAwait(true);
            StatusText = "Tagged " + ids.Count + " prompt(s).";
        }
        catch (Exception ex)
        {
            StatusText = "Bulk add-tag failed: " + ex.Message;
        }
    }
}