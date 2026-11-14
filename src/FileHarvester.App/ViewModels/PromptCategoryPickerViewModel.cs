// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/PromptCategoryPickerViewModel.cs
// PURPOSE:  The bulk-move category picker's view model. Two dropdowns: the
//           categories and the selected category's sub-categories. The OK
//           returns the (CategoryId, SubCategoryId) pair; the Cancel closes
//           with no result. A null CategoryId means the prompt moves to the
//           uncategorized bucket.
// RELATION: Constructed by PromptPage.axaml.cs's OnBulkMoveRequested; the
//           result is passed to PromptListViewModel.BulkMoveAsync. The
//           Categories/SubCategories/SelectedCategory/SelectedSubCategory
//           fields mirror PromptEditorViewModel's picker (the P2.5's).
// NOTES:    - The SelectedCategory's setter clears the SelectedSubCategory
//             and reloads the SubCategories (the editor's exact pattern).
//           - The SelectedCategory may be null (the uncategorized); the OK's
//             the null's the uncategorized.
//           - The load and the reload are fire-and-forget; the errors set
//             the StatusText.
// ============================================================================

using System.Collections.ObjectModel;
using Prompt.Core.Domain;

namespace FileHarvester.App.ViewModels;

/// <summary>The category picker's result: the chosen category/sub-category, either may be null.</summary>
public sealed record PromptCategoryPickerResult(Guid? CategoryId, Guid? SubCategoryId);

/// <summary>The bulk-move category picker.</summary>
public sealed class PromptCategoryPickerViewModel : ViewModelBase
{
    private readonly ICategoryStore _categories;
    private readonly ISubCategoryStore _subCategories;

    private Category? _selectedCategory;
    private SubCategory? _selectedSubCategory;
    private string? _statusText;

    /// <summary>Creates the picker.</summary>
    public PromptCategoryPickerViewModel(ICategoryStore categories, ISubCategoryStore subCategories)
    {
        _categories = categories ?? throw new ArgumentNullException(nameof(categories));
        _subCategories = subCategories ?? throw new ArgumentNullException(nameof(subCategories));

        OkCommand = new RelayCommand(
            () => Saved?.Invoke(this, new PromptCategoryPickerResult(SelectedCategory?.Id, SelectedSubCategory?.Id)));
        CancelCommand = new RelayCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));

        _ = LoadCategoriesAsync();
    }

    /// <summary>The categories.</summary>
    public ObservableCollection<Category> Categories { get; } = new();

    /// <summary>The selected category, or null (the uncategorized).</summary>
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

    /// <summary>The selected category's sub-categories.</summary>
    public ObservableCollection<SubCategory> SubCategories { get; } = new();

    /// <summary>The selected sub-category, or null.</summary>
    public SubCategory? SelectedSubCategory
    {
        get => _selectedSubCategory;
        set => SetProperty(ref _selectedSubCategory, value);
    }

    /// <summary>A short status message.</summary>
    public string? StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>Returns the current selection via the Saved event.</summary>
    public RelayCommand OkCommand { get; }

    /// <summary>Closes with no result.</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>Raised when OK is clicked with the result.</summary>
    public event EventHandler<PromptCategoryPickerResult>? Saved;

    /// <summary>Raised when Cancel is clicked.</summary>
    public event EventHandler? Cancelled;

    private async Task LoadCategoriesAsync()
    {
        try
        {
            var cats = await _categories.ListAsync().ConfigureAwait(true);
            Categories.Clear();
            foreach (var c in cats) Categories.Add(c);
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
}