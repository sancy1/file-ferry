// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/CategoryManagerViewModel.cs
// PURPOSE:  The category manager's view model. Lists the categories and the
//           selected category's sub-categories; create, rename, reorder,
//           delete. The delete's cascade (P1.2) moves prompts to
//           un-categorized. Raises Changed on every mutation; the page
//           reloads the sidebar.
// RELATION: Opened by PromptPage.axaml.cs when the sidebar's Manage button is
//           clicked. Bound to CategoryManagerDialog.axaml.
// NOTES:    - The reorder's two UpdateAsync calls are not atomic. A failed
//             second call leaves the order half-swapped. Recorded as F-248;
//             a transactional reorder is a P4.x or P5.x enhancement.
//           - Archive is not in the v1 schema. Recorded as F-249; deferred to
//             a P4.x or P5.x backfill with an IsArchived column.
//           - The delete uses ICategoryStore.DeleteAsync / ISubCategoryStore.
//           - F-250 closed: rename works via RenameName + IsRenaming +
//             SaveRenameCommand; the dialog TextBox + Save/Cancel are
//             gated on IsRenaming.
//             DeleteAsync. The P1.2 cascade sets prompts' CategoryId to null.
// ============================================================================

using System.Collections.ObjectModel;
using Prompt.Core.Domain;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The category manager's view model.
/// </summary>
public sealed class CategoryManagerViewModel : ViewModelBase
{
    private readonly ICategoryStore _categories;
    private readonly ISubCategoryStore _subCategories;

    private Category? _selectedCategory;
    private SubCategory? _selectedSubCategory;
    private string? _newCategoryName;
    private string? _newSubCategoryName;
    private string? _statusText;
    private string? _renameName;
    private bool _isRenaming;

    /// <summary>Creates the manager VM.</summary>
    public CategoryManagerViewModel(ICategoryStore categories, ISubCategoryStore subCategories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(subCategories);
        _categories = categories;
        _subCategories = subCategories;

        CreateCategoryCommand = new AsyncRelayCommand(CreateCategoryAsync);
        RenameCategoryCommand = new AsyncRelayCommand(RenameCategoryAsync);
        DeleteCategoryCommand = new AsyncRelayCommand(DeleteCategoryAsync);
        MoveCategoryUpCommand = new AsyncRelayCommand(() => MoveCategoryAsync(-1));
        MoveCategoryDownCommand = new AsyncRelayCommand(() => MoveCategoryAsync(1));
        CreateSubCategoryCommand = new AsyncRelayCommand(CreateSubCategoryAsync);
        RenameSubCategoryCommand = new AsyncRelayCommand(RenameSubCategoryAsync);
        DeleteSubCategoryCommand = new AsyncRelayCommand(DeleteSubCategoryAsync);
        MoveSubCategoryUpCommand = new AsyncRelayCommand(() => MoveSubCategoryAsync(-1));
        MoveSubCategoryDownCommand = new AsyncRelayCommand(() => MoveSubCategoryAsync(1));
        CloseCommand = new RelayCommand(() => Closed?.Invoke(this, EventArgs.Empty));
        SaveRenameCommand = new AsyncRelayCommand(SaveRenameAsync);
        CancelRenameCommand = new RelayCommand(CancelRename);

        _ = LoadAsync();
    }

    public ObservableCollection<Category> Categories { get; } = new();
    public ObservableCollection<SubCategory> SubCategories { get; } = new();

    public Category? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                _selectedSubCategory = null;
                OnPropertyChanged(nameof(SelectedSubCategory));
                _ = LoadSubCategoriesAsync();
            }
        }
    }

    public SubCategory? SelectedSubCategory
    {
        get => _selectedSubCategory;
        set => SetProperty(ref _selectedSubCategory, value);
    }

    public string? NewCategoryName
    {
        get => _newCategoryName;
        set => SetProperty(ref _newCategoryName, value);
    }

    public string? NewSubCategoryName
    {
        get => _newSubCategoryName;
        set => SetProperty(ref _newSubCategoryName, value);
    }

    /// <summary>The in-progress new name while a rename is open; null otherwise.</summary>
    public string? RenameName
    {
        get => _renameName;
        set => SetProperty(ref _renameName, value);
    }

    /// <summary>True while a rename edit is open; the dialog shows the rename TextBox.</summary>
    public bool IsRenaming
    {
        get => _isRenaming;
        set => SetProperty(ref _isRenaming, value);
    }

    public string? StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public AsyncRelayCommand CreateCategoryCommand { get; }
    public AsyncRelayCommand RenameCategoryCommand { get; }
    public AsyncRelayCommand DeleteCategoryCommand { get; }
    public AsyncRelayCommand MoveCategoryUpCommand { get; }
    public AsyncRelayCommand MoveCategoryDownCommand { get; }
    public AsyncRelayCommand CreateSubCategoryCommand { get; }
    public AsyncRelayCommand RenameSubCategoryCommand { get; }
    public AsyncRelayCommand DeleteSubCategoryCommand { get; }
    public AsyncRelayCommand MoveSubCategoryUpCommand { get; }
    public AsyncRelayCommand MoveSubCategoryDownCommand { get; }
    public RelayCommand CloseCommand { get; }

    /// <summary>Commits the rename (calls RenameCategoryAsync / RenameSubCategoryAsync in commit mode).</summary>
    public AsyncRelayCommand SaveRenameCommand { get; }

    /// <summary>Cancels the rename edit: clears RenameName, sets IsRenaming false.</summary>
    public RelayCommand CancelRenameCommand { get; }

    /// <summary>Raised when a mutation succeeds; the page reloads the sidebar.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when Close is clicked.</summary>
    public event EventHandler? Closed;

    private async Task LoadAsync()
    {
        try
        {
            var cats = await _categories.ListAsync().ConfigureAwait(true);
            Categories.Clear();
            foreach (var c in cats) Categories.Add(c);
        }
        catch (Exception ex) { StatusText = "Could not load categories: " + ex.Message; }
    }

    private async Task LoadSubCategoriesAsync()
    {
        SubCategories.Clear();
        if (SelectedCategory is null) return;
        try
        {
            var subs = await _subCategories.ListAsync(SelectedCategory.Id).ConfigureAwait(true);
            foreach (var s in subs) SubCategories.Add(s);
        }
        catch (Exception ex) { StatusText = "Could not load sub-categories: " + ex.Message; }
    }

    private async Task CreateCategoryAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCategoryName)) { StatusText = "Enter a category name."; return; }
        var now = DateTimeOffset.UtcNow;
        var cat = new Category(Guid.NewGuid(), NewCategoryName.Trim(), null, Categories.Count, now, now);
        try
        {
            await _categories.CreateAsync(cat).ConfigureAwait(true);
            NewCategoryName = null;
            await LoadAsync().ConfigureAwait(true);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { StatusText = "Could not create: " + ex.Message; }
    }

    /// <summary>
    /// Begins or commits a category rename. On first call (IsRenaming false),
    /// sets RenameName to the selected category's name and IsRenaming true;
    /// the dialog shows the rename TextBox. On second call (IsRenaming true),
    /// commits the trimmed RenameName via ICategoryStore.UpdateAsync, reloads,
    /// raises Changed. No-op with a status message if no category is selected
    /// or RenameName is empty.
    /// </summary>
    private async Task RenameCategoryAsync()
    {
        if (SelectedCategory is null) { StatusText = "Select a category to rename."; return; }
        if (!IsRenaming)
        {
            RenameName = SelectedCategory.Name;
            IsRenaming = true;
            StatusText = "Editing: " + SelectedCategory.Name;
            return;
        }
        if (string.IsNullOrWhiteSpace(RenameName)) { StatusText = "Enter a new name."; return; }
        var now = DateTimeOffset.UtcNow;
        var updated = SelectedCategory with { Name = RenameName.Trim(), UpdatedAt = now };
        var id = SelectedCategory.Id;
        try
        {
            await _categories.UpdateAsync(updated).ConfigureAwait(true);
            IsRenaming = false;
            RenameName = null;
            await LoadAsync().ConfigureAwait(true);
            SelectedCategory = Categories.FirstOrDefault(c => c.Id == id);
            StatusText = "Renamed.";
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { StatusText = "Could not rename: " + ex.Message; }
    }

    private async Task DeleteCategoryAsync()
    {
        if (SelectedCategory is null) { StatusText = "Select a category to delete."; return; }
        try
        {
            await _categories.DeleteAsync(SelectedCategory.Id).ConfigureAwait(true);
            SelectedCategory = null;
            await LoadAsync().ConfigureAwait(true);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { StatusText = "Could not delete: " + ex.Message; }
    }

    private async Task MoveCategoryAsync(int direction)
    {
        if (SelectedCategory is null) return;
        var idx = Categories.IndexOf(SelectedCategory);
        if (idx < 0) return;
        var target = idx + direction;
        if (target < 0 || target >= Categories.Count) return;
        var a = Categories[idx];
        var b = Categories[target];
        var now = DateTimeOffset.UtcNow;
        try
        {
            await _categories.UpdateAsync(a with { SortOrder = b.SortOrder, UpdatedAt = now }).ConfigureAwait(true);
            await _categories.UpdateAsync(b with { SortOrder = a.SortOrder, UpdatedAt = now }).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            SelectedCategory = Categories.FirstOrDefault(c => c.Id == a.Id);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { StatusText = "Could not reorder: " + ex.Message; }
    }

    private async Task CreateSubCategoryAsync()
    {
        if (SelectedCategory is null) { StatusText = "Select a category first."; return; }
        if (string.IsNullOrWhiteSpace(NewSubCategoryName)) { StatusText = "Enter a sub-category name."; return; }
        var now = DateTimeOffset.UtcNow;
        var sub = new SubCategory(Guid.NewGuid(), SelectedCategory.Id, NewSubCategoryName.Trim(), null, SubCategories.Count, now, now);
        try
        {
            await _subCategories.CreateAsync(sub).ConfigureAwait(true);
            NewSubCategoryName = null;
            await LoadSubCategoriesAsync().ConfigureAwait(true);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { StatusText = "Could not create sub-category: " + ex.Message; }
    }

    /// <summary>
    /// Begins or commits a sub-category rename. Same two-step pattern as
    /// RenameCategoryAsync: first call sets RenameName/IsRenaming; second call
    /// commits via ISubCategoryStore.UpdateAsync, reloads, raises Changed.
    /// </summary>
    private async Task RenameSubCategoryAsync()
    {
        if (SelectedSubCategory is null) { StatusText = "Select a sub-category to rename."; return; }
        if (!IsRenaming)
        {
            RenameName = SelectedSubCategory.Name;
            IsRenaming = true;
            StatusText = "Editing: " + SelectedSubCategory.Name;
            return;
        }
        if (string.IsNullOrWhiteSpace(RenameName)) { StatusText = "Enter a new name."; return; }
        var now = DateTimeOffset.UtcNow;
        var updated = SelectedSubCategory with { Name = RenameName.Trim(), UpdatedAt = now };
        var id = SelectedSubCategory.Id;
        try
        {
            await _subCategories.UpdateAsync(updated).ConfigureAwait(true);
            IsRenaming = false;
            RenameName = null;
            await LoadSubCategoriesAsync().ConfigureAwait(true);
            SelectedSubCategory = SubCategories.FirstOrDefault(s => s.Id == id);
            StatusText = "Renamed.";
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { StatusText = "Could not rename sub-category: " + ex.Message; }
    }

    private async Task DeleteSubCategoryAsync()
    {
        if (SelectedSubCategory is null) { StatusText = "Select a sub-category to delete."; return; }
        try
        {
            await _subCategories.DeleteAsync(SelectedSubCategory.Id).ConfigureAwait(true);
            SelectedSubCategory = null;
            await LoadSubCategoriesAsync().ConfigureAwait(true);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { StatusText = "Could not delete sub-category: " + ex.Message; }
    }

    private async Task MoveSubCategoryAsync(int direction)
    {
        if (SelectedSubCategory is null || SelectedCategory is null) return;
        var idx = SubCategories.IndexOf(SelectedSubCategory);
        if (idx < 0) return;
        var target = idx + direction;
        if (target < 0 || target >= SubCategories.Count) return;
        var a = SubCategories[idx];
        var b = SubCategories[target];
        var now = DateTimeOffset.UtcNow;
        try
        {
            await _subCategories.UpdateAsync(a with { SortOrder = b.SortOrder, UpdatedAt = now }).ConfigureAwait(true);
            await _subCategories.UpdateAsync(b with { SortOrder = a.SortOrder, UpdatedAt = now }).ConfigureAwait(true);
            await LoadSubCategoriesAsync().ConfigureAwait(true);
            SelectedSubCategory = SubCategories.FirstOrDefault(s => s.Id == a.Id);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { StatusText = "Could not reorder sub-category: " + ex.Message; }
    }

    /// <summary>
    /// Commits the in-progress rename. Routes to the category or sub-category
    /// path based on which is selected; the dialog's Save button invokes it.
    /// </summary>
    private async Task SaveRenameAsync()
    {
        if (SelectedSubCategory is not null) { await RenameSubCategoryAsync().ConfigureAwait(true); return; }
        if (SelectedCategory is not null) { await RenameCategoryAsync().ConfigureAwait(true); return; }
        StatusText = "Nothing selected to rename.";
    }

    /// <summary>Cancels the rename edit: clears RenameName, sets IsRenaming false.</summary>
    private void CancelRename()
    {
        RenameName = null;
        IsRenaming = false;
        StatusText = null;
    }
}