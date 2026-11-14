// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/PromptImportDialogViewModel.cs
// PURPOSE:  The import dialog's view model. Holds the selected source file,
//           the category and sub-category pickers (the same pattern as the
//           editor), the Import / Cancel commands, and the Saved / Cancelled
//           events. Calls IPromptImporter.ImportFileAsync.
// RELATION: Constructed by PromptPage.axaml.cs when the + Import button is
//           clicked. Bound to PromptImportDialog.axaml. Uses IPromptImporter
//           (P3.5), ICategoryStore, and ISubCategoryStore.
// NOTES:    - The source file is chosen via the code-behind's native file
//             picker; the VM's SourceFilePath property is set by the
//             code-behind.
//           - The category/sub-category pickers mirror the editor's: the
//             category load is fire-and-forget; the selected category's
//             sub-categories reload on change.
//           - The ImportCommand calls _importer.ImportFileAsync with the
//             current file path and the selected category/sub-category ids.
//             On success the dialog closes with the ImportResult; on
//             failure the StatusText shows the reason and the dialog stays
//             open.
//           - The dialog is a modal: the user can browse for a different
//             file, change the pickers, and retry.
// ============================================================================

using System.Collections.ObjectModel;
using Prompt.Core.Domain;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The import dialog's view model.
/// </summary>
public sealed class PromptImportDialogViewModel : ViewModelBase
{
    private readonly IPromptImporter _importer;
    private readonly ICategoryStore _categories;
    private readonly ISubCategoryStore _subCategories;

    private string _sourceFilePath = string.Empty;
    private Category? _selectedCategory;
    private SubCategory? _selectedSubCategory;
    private string? _statusText;
    private bool _isImporting;

    /// <summary>Creates the dialog VM.</summary>
    public PromptImportDialogViewModel(
        IPromptImporter importer,
        ICategoryStore categories,
        ISubCategoryStore subCategories)
    {
        _importer = importer ?? throw new ArgumentNullException(nameof(importer));
        _categories = categories ?? throw new ArgumentNullException(nameof(categories));
        _subCategories = subCategories ?? throw new ArgumentNullException(nameof(subCategories));

        ImportCommand = new AsyncRelayCommand(OnImportAsync);
        CancelCommand = new RelayCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));
        BrowseCommand = new RelayCommand(() => { /* code-behind */ });

        _ = LoadCategoriesAsync();
    }

    /// <summary>The selected source file path (set by the code-behind's picker).</summary>
    public string SourceFilePath
    {
        get => _sourceFilePath;
        set
        {
            if (SetProperty(ref _sourceFilePath, value))
            {
                OnPropertyChanged(nameof(CanImport));
            }
        }
    }

    /// <summary>The categories for the picker.</summary>
    public ObservableCollection<Category> Categories { get; } = new();

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

    /// <summary>The sub-categories for the selected category.</summary>
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

    /// <summary>True while the import is running.</summary>
    public bool IsImporting
    {
        get => _isImporting;
        private set
        {
            if (SetProperty(ref _isImporting, value))
            {
                OnPropertyChanged(nameof(CanImport));
            }
        }
    }

    /// <summary>True when the Import button is enabled.</summary>
    public bool CanImport => !_isImporting && !string.IsNullOrWhiteSpace(_sourceFilePath);

    /// <summary>Imports the file.</summary>
    public AsyncRelayCommand ImportCommand { get; }

    /// <summary>Closes the dialog without importing.</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>The code-behind's file picker hook.</summary>
    public RelayCommand BrowseCommand { get; }

    /// <summary>Raised on a successful import; the dialog closes with the result.</summary>
    public event EventHandler<ImportResult>? Saved;

    /// <summary>Raised on Cancel; the dialog closes with null.</summary>
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

    private async Task OnImportAsync()
    {
        if (!CanImport) return;
        IsImporting = true;
        StatusText = "Importing...";
        try
        {
            var result = await _importer.ImportFileAsync(
                _sourceFilePath,
                SelectedCategory?.Id,
                SelectedSubCategory?.Id).ConfigureAwait(true);

            if (result.Success)
            {
                StatusText = "Imported: " + (result.Prompt?.Title ?? "(unknown)");
                Saved?.Invoke(this, result);
            }
            else
            {
                StatusText = "Import failed: " + (result.ErrorMessage ?? "unknown");
            }
        }
        catch (Exception ex)
        {
            StatusText = "Import failed: " + ex.Message;
        }
        finally
        {
            IsImporting = false;
        }
    }
}