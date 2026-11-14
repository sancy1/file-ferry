// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/PromptEditorViewModel.cs
// PURPOSE:  The prompt editor's view model. Holds the title, body, category,
//           sub-category, and tags; parses the tags on save; calls
//           IPromptStore.CreateAsync (create mode) or UpdateAsync (edit mode,
//           PUT semantics); raises Saved with the created or updated prompt.
// RELATION: Constructed by PromptPage.axaml.cs when the + New Prompt button
//           or the Edit button is clicked. Bound to PromptEditorDialog.axaml.
// NOTES:    - IsNew distinguishes create from edit. The constructor takes an
//             existing PromptRecord for edit; null for create.
//           - SelectedCategory's setter reloads SubCategories from
//             ISubCategoryStore.ListAsync (fire-and-forget with status text).
//           - CanSave requires Title and Body non-empty.
//           - The Tags field is a comma-separated text; ParseTags splits,
//             trims, and drops empties.
//           - No confirmation dialog; Save is the user's confirmation.
// ============================================================================

using System.Collections.ObjectModel;
using Prompt.Core.Domain;
using PromptRecord = Prompt.Core.Domain.Prompt;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The prompt editor's view model. Create or edit a prompt.
/// </summary>
public sealed class PromptEditorViewModel : ViewModelBase, IDisposable
{
    private readonly IPromptStore _prompts;
    private readonly ICategoryStore _categories;
    private readonly ISubCategoryStore _subCategories;
    private readonly PromptRecord? _existing;
    private readonly Guid _promptId;
    private CancellationTokenSource? _debounceCts;

    private string _title = string.Empty;
    private string _body = string.Empty;
    private Category? _selectedCategory;
    private SubCategory? _selectedSubCategory;
    private string _tagsText = string.Empty;
    private bool _isFavorite;
    private string? _statusText;

    /// <summary>
    /// Creates the editor VM.
    /// </summary>
    /// <param name="prompts">The prompt store. Never null.</param>
    /// <param name="categories">The category store. Never null.</param>
    /// <param name="subCategories">The sub-category store. Never null.</param>
    /// <param name="existing">The prompt to edit, or null for create.</param>
    public PromptEditorViewModel(
        IPromptStore prompts,
        ICategoryStore categories,
        ISubCategoryStore subCategories,
        PromptRecord? existing)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(subCategories);
        _prompts = prompts;
        _categories = categories;
        _subCategories = subCategories;
        _existing = existing;
        _promptId = existing?.Id ?? Guid.NewGuid();
        IsNew = existing is null;
        if (existing is not null)
        {
            _title = existing.Title;
            _body = existing.Body;
            _tagsText = string.Join(", ", existing.Tags);
            _isFavorite = existing.IsFavorite;
        }
        SaveCommand = new AsyncRelayCommand(OnSaveAsync);
        CancelCommand = new RelayCommand(() =>
        {
            _ = _prompts.ClearDraftAsync(_promptId);
            Cancelled?.Invoke(this, EventArgs.Empty);
        });
        _ = LoadCategoriesAsync();
        _ = LoadDraftAsync();
    }

    /// <summary>True when creating a new prompt.</summary>
    public bool IsNew { get; }

    /// <summary>The dialog's title: "Create Prompt" or "Edit Prompt".</summary>
    public string DialogTitle => IsNew ? "Create Prompt" : "Edit Prompt";

    /// <summary>The prompt's title.</summary>
    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value))
            {
                OnPropertyChanged(nameof(CanSave));
                StartDebounce();
            }
        }
    }

    /// <summary>The prompt's body.</summary>
    public string Body
    {
        get => _body;
        set
        {
            if (SetProperty(ref _body, value))
            {
                OnPropertyChanged(nameof(CanSave));
                StartDebounce();
            }
        }
    }

    /// <summary>The selected category, or null.</summary>
    public Category? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                _selectedSubCategory = null;
                OnPropertyChanged(nameof(SelectedSubCategory));
                _ = ReloadSubCategoriesAsync();
            }
        }
    }

    /// <summary>The selected sub-category, or null.</summary>
    public SubCategory? SelectedSubCategory
    {
        get => _selectedSubCategory;
        set => SetProperty(ref _selectedSubCategory, value);
    }

    /// <summary>The tags as comma-separated text.</summary>
    public string TagsText
    {
        get => _tagsText;
        set => SetProperty(ref _tagsText, value);
    }

    /// <summary>The favorite flag. Preserved on edit; set on create.</summary>
    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetProperty(ref _isFavorite, value);
    }

    /// <summary>A short status message, or null.</summary>
    public string? StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>The categories list for the dropdown.</summary>
    public ObservableCollection<Category> Categories { get; } = new();

    /// <summary>The sub-categories of the selected category.</summary>
    public ObservableCollection<SubCategory> SubCategories { get; } = new();

    /// <summary>True when Title and Body are non-empty.</summary>
    public bool CanSave => !string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(Body);

    /// <summary>Saves the prompt. Create or edit.</summary>
    public AsyncRelayCommand SaveCommand { get; }

    /// <summary>Closes the dialog without saving.</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>Raised when the save succeeds with the created or updated prompt.</summary>
    public event EventHandler<PromptRecord>? Saved;

    /// <summary>Raised when Cancel is clicked.</summary>
    public event EventHandler? Cancelled;

    private async Task LoadCategoriesAsync()
    {
        try
        {
            var cats = await _categories.ListAsync().ConfigureAwait(true);
            Categories.Clear();
            foreach (var c in cats) Categories.Add(c);
            if (_existing?.CategoryId is not null)
            {
                var cat = cats.FirstOrDefault(c => c.Id == _existing.CategoryId);
                if (cat is not null)
                {
                    _selectedCategory = cat;
                    OnPropertyChanged(nameof(SelectedCategory));
                    await ReloadSubCategoriesAsync().ConfigureAwait(true);
                    if (_existing.SubCategoryId is not null)
                    {
                        var sub = SubCategories.FirstOrDefault(s => s.Id == _existing.SubCategoryId);
                        if (sub is not null)
                        {
                            _selectedSubCategory = sub;
                            OnPropertyChanged(nameof(SelectedSubCategory));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            StatusText = "Could not load categories: " + ex.Message;
        }
    }

    private async Task ReloadSubCategoriesAsync()
    {
        SubCategories.Clear();
        if (SelectedCategory is null) return;
        try
        {
            var subs = await _subCategories.ListAsync(SelectedCategory.Id).ConfigureAwait(true);
            foreach (var s in subs) SubCategories.Add(s);
        }
        catch (Exception ex)
        {
            StatusText = "Could not load sub-categories: " + ex.Message;
        }
    }

    private async Task OnSaveAsync()
    {
        if (!CanSave) return;
        var tags = ParseTags();
        var now = DateTimeOffset.UtcNow;
        try
        {
            PromptRecord result;
            if (IsNew)
            {
                var newPrompt = new PromptRecord(
                    Guid.NewGuid(),
                    Title.Trim(),
                    Body,
                    BodyFormat.PlainText,
                    SelectedCategory?.Id,
                    SelectedSubCategory?.Id,
                    null,
                    IsFavorite,
                    false,
                    tags,
                    now,
                    now);
                result = await _prompts.CreateAsync(newPrompt).ConfigureAwait(true);
            }
            else
            {
                var updated = _existing! with
                {
                    Title = Title.Trim(),
                    Body = Body,
                    CategoryId = SelectedCategory?.Id,
                    SubCategoryId = SelectedSubCategory?.Id,
                    Tags = tags,
                    IsFavorite = IsFavorite,
                    UpdatedAt = now,
                };
                result = await _prompts.UpdateAsync(updated).ConfigureAwait(true);
            }
            await _prompts.ClearDraftAsync(_promptId).ConfigureAwait(true);
            Saved?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            StatusText = "Could not save: " + ex.Message;
        }
    }

    private IReadOnlyList<string> ParseTags()
    {
        if (string.IsNullOrWhiteSpace(TagsText)) return Array.Empty<string>();
        return TagsText
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void StartDebounce()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        var ct = _debounceCts.Token;
        _ = DebouncedSaveDraftAsync(ct);
    }

    private async Task DebouncedSaveDraftAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(2000, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (ct.IsCancellationRequested) return;
        try
        {
            var draft = new PromptDraft(_promptId, _title, _body, DateTimeOffset.UtcNow);
            await _prompts.SaveDraftAsync(draft, ct).ConfigureAwait(true);
        }
        catch
        {
            // Best-effort draft; a save failure must not surface as an editor error.
        }
    }

    private async Task LoadDraftAsync()
    {
        try
        {
            var draft = await _prompts.GetDraftAsync(_promptId).ConfigureAwait(true);
            if (draft is null) return;
            if (draft.Title == _title && draft.Body == _body) return;
            _title = draft.Title ?? _title;
            _body = draft.Body ?? _body;
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Body));
            OnPropertyChanged(nameof(CanSave));
            StatusText = "Restored unsaved draft from " + draft.SavedAt.ToString("g", System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            // Best-effort draft restore; a load failure must not surface as an editor error.
        }
    }

    /// <summary>
    /// Cancels and disposes the debounce CancellationTokenSource. Called by
    /// the dialog when it closes (the Saved/Cancelled). No other resource is
    /// owned; the stores are injected singletons.
    /// </summary>
    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
        GC.SuppressFinalize(this);
    }
}