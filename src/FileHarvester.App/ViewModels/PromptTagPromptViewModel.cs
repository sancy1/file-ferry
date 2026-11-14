// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/PromptTagPromptViewModel.cs
// PURPOSE:  The bulk add-tag prompt's view model. A single Tag string; the OK
//           returns the trimmed tag; the Cancel closes with no result.
// RELATION: Constructed by PromptPage.axaml.cs's OnBulkAddTagRequested; the
//           result is passed to PromptListViewModel.BulkAddTagAsync.
// NOTES:    - The OK is enabled when the Tag is non-empty (the CanAdd).
//           - The Tag's setter raises CanAdd and the OK's CanExecute.
// ============================================================================

namespace FileHarvester.App.ViewModels;

/// <summary>The bulk add-tag prompt.</summary>
public sealed class PromptTagPromptViewModel : ViewModelBase
{
    private string _tag = string.Empty;

    /// <summary>Creates the prompt.</summary>
    public PromptTagPromptViewModel()
    {
        OkCommand = new RelayCommand(
            () => Saved?.Invoke(this, _tag.Trim()),
            () => CanAdd);
        CancelCommand = new RelayCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>The tag to add.</summary>
    public string Tag
    {
        get => _tag;
        set
        {
            if (SetProperty(ref _tag, value))
            {
                OnPropertyChanged(nameof(CanAdd));
                ((RelayCommand)OkCommand).RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>True when the Tag is non-empty.</summary>
    public bool CanAdd => !string.IsNullOrWhiteSpace(_tag);

    /// <summary>Returns the tag via the Saved event.</summary>
    public RelayCommand OkCommand { get; }

    /// <summary>Closes with no result.</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>Raised when OK is clicked with the trimmed tag.</summary>
    public event EventHandler<string>? Saved;

    /// <summary>Raised when Cancel is clicked.</summary>
    public event EventHandler? Cancelled;
}