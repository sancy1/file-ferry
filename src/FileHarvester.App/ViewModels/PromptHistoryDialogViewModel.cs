// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/PromptHistoryDialogViewModel.cs
// PURPOSE:  The version history dialog's view model. Loads the prompt's
//           version snapshots (GetVersionsAsync, capped at 50 by the store),
//           shows the selected version's title/body in the preview, and
//           restores a selected version (RestoreVersionAsync).
// RELATION: Constructed by PromptPage.axaml.cs when the viewer's History
//           button is clicked. Bound to PromptHistoryDialog.axaml. Uses
//           IPromptVersionStore (P1.4) and the PromptVersion record.
// NOTES:    - The store caps GetVersionsAsync at 50; the status shows the
//             count and notes the cap when 50.
//           - The preview binds the selected version's Title/Body; the
//             dialog's ScrollViewer scrolls a long body (the F-276 fix).
//           - The RestoreCommand calls RestoreVersionAsync(promptId,
//             versionId); the store writes the snapshot's Title/Body back
//             to the prompt. The Saved event closes the dialog; the page
//             reloads the list and refreshes the viewer.
//           - The list's ListBoxItem style is the sidebar's density
//             (Padding=8,1, MinHeight=0 - the F-272/F-202 fix).
// ============================================================================

using System.Collections.ObjectModel;
using Prompt.Core.Domain;
using PromptRecord = Prompt.Core.Domain.Prompt;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The version history dialog's view model.
/// </summary>
public sealed class PromptHistoryDialogViewModel : ViewModelBase
{
    private readonly IPromptVersionStore _versions;
    private readonly Guid _promptId;
    private readonly string _promptTitle;

    private PromptVersion? _selectedVersion;
    private string _previewTitle = string.Empty;
    private string _previewBody = string.Empty;
    private string? _statusText;
    private bool _isBusy;

    /// <summary>Creates the history VM.</summary>
    public PromptHistoryDialogViewModel(IPromptVersionStore versions, PromptRecord prompt)
    {
        _versions = versions ?? throw new ArgumentNullException(nameof(versions));
        ArgumentNullException.ThrowIfNull(prompt);
        _promptId = prompt.Id;
        _promptTitle = prompt.Title;

        RestoreCommand = new AsyncRelayCommand(RestoreAsync);
        ClearHistoryCommand = new AsyncRelayCommand(ClearHistoryAsync);
        CancelCommand = new RelayCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));

        _ = LoadAsync();
    }

    /// <summary>The current prompt's title (the dialog's subtitle).</summary>
    public string PromptTitle => _promptTitle;

    /// <summary>The versions, newest first (the store's order).</summary>
    public ObservableCollection<PromptVersion> Versions { get; } = new();

    /// <summary>The selected version, or null.</summary>
    public PromptVersion? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (SetProperty(ref _selectedVersion, value))
            {
                PreviewTitle = value?.Title ?? string.Empty;
                PreviewBody = value?.Body ?? string.Empty;
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    /// <summary>True when a version is selected.</summary>
    public bool HasSelection => _selectedVersion is not null;

    /// <summary>The selected version's title (the preview header).</summary>
    public string PreviewTitle
    {
        get => _previewTitle;
        private set => SetProperty(ref _previewTitle, value);
    }

    /// <summary>The selected version's body (the preview content).</summary>
    public string PreviewBody
    {
        get => _previewBody;
        private set => SetProperty(ref _previewBody, value);
    }

    /// <summary>A short status message.</summary>
    public string? StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>True while loading or restoring.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>Restores the selected version.</summary>
    public AsyncRelayCommand RestoreCommand { get; }

    /// <summary>Clears every version of the prompt (the history only).</summary>
    public AsyncRelayCommand ClearHistoryCommand { get; }

    /// <summary>Closes the dialog.</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>Raised after a successful restore; the page reloads and refreshes.</summary>
    public event EventHandler? Saved;

    /// <summary>Raised when the dialog is cancelled.</summary>
    public event EventHandler? Cancelled;

    /// <summary>Loads the versions into Versions.</summary>
    public async Task LoadAsync()
    {
        IsBusy = true;
        StatusText = "Loading history...";
        try
        {
            var versions = await _versions.GetVersionsAsync(_promptId).ConfigureAwait(true);
            Versions.Clear();
            foreach (var v in versions) Versions.Add(v);
            StatusText = Versions.Count == 0
                ? "No version history yet. Edit the prompt to create the first snapshot."
                : Versions.Count + " version(s)" + (Versions.Count >= 50 ? " (the store's cap)." : ".");
            if (Versions.Count > 0) { SelectedVersion = Versions[0]; }
        }
        catch (Exception ex)
        {
            StatusText = "Could not load the history: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RestoreAsync()
    {
        if (SelectedVersion is null) { StatusText = "Select a version to restore."; return; }
        var versionId = SelectedVersion.Id;
        IsBusy = true;
        StatusText = "Restoring...";
        try
        {
            await _versions.RestoreVersionAsync(_promptId, versionId).ConfigureAwait(true);
            StatusText = "Restored.";
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusText = "Could not restore: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Deletes every version of the prompt and reloads.</summary>
    public async Task ClearHistoryAsync()
    {
        IsBusy = true;
        StatusText = "Clearing history...";
        try
        {
            await _versions.DeleteAllVersionsAsync(_promptId).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
            StatusText = "History cleared. The current prompt body is unchanged.";
        }
        catch (Exception ex)
        {
            StatusText = "Could not clear the history: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}