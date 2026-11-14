// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/CodeViewerViewModel.cs
// PURPOSE:  The CodeViewer workspace host. Owns the project tab strip:
//           opening local project folders as tabs, closing tabs, and
//           tracking the active tab. Each tab is an independent
//           ProjectTabViewModel with its own selection engine, so one
//           project's state never leaks into another.
// RELATION: Instantiated by MainWindowViewModel with services resolved
//           from App.Services. The folder picker lives in
//           CodeViewerPage code-behind (the established convention);
//           this view model only receives the chosen path. Bound by
//           CodeViewerPage.axaml.
// ============================================================================

using System.Collections.ObjectModel;
using System.Windows.Input;
using CodeViewer.Core.Domain;
using CodeViewer.Platform.FileSystem;
using CodeViewer.Platform.State;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The CodeViewer page view model: a strip of project tabs.
/// </summary>
public sealed class CodeViewerViewModel : ViewModelBase
{
    private readonly ProjectTreeEnumerator _enumerator;
    private readonly ProjectFileReader _reader;
    private readonly IWorkspaceStateStore _stateStore;
    private readonly Func<string, System.Threading.Tasks.Task> _copyToClipboard;
    private readonly IGlobalExclusionStore? _exclusionStore;
    private readonly GlobalExclusionsHolder? _exclusionsHolder;

    private ProjectTabViewModel? _activeTab;
    private string _statusText = string.Empty;

    public CodeViewerViewModel(
        ProjectTreeEnumerator enumerator,
        ProjectFileReader reader,
        IWorkspaceStateStore stateStore,
        Func<string, System.Threading.Tasks.Task> copyToClipboard,
        IGlobalExclusionStore? exclusionStore = null,
        GlobalExclusionsHolder? exclusionsHolder = null)
    {
        _enumerator = enumerator ?? throw new ArgumentNullException(nameof(enumerator));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _copyToClipboard = copyToClipboard ?? throw new ArgumentNullException(nameof(copyToClipboard));
        _exclusionStore = exclusionStore;
        _exclusionsHolder = exclusionsHolder;

        CloseTabCommand = new RelayCommand<ProjectTabViewModel>(CloseTab);
        SelectTabCommand = new RelayCommand<ProjectTabViewModel>(tab =>
        {
            if (tab is not null)
            {
                ActiveTab = tab;
            }
        });
        ManageExclusionsCommand = new RelayCommand(() => RequestManageExclusions?.Invoke(this, EventArgs.Empty));

        // Load the persisted exclusion rules at startup so they
        // survive application restarts. Fire-and-forget: the ctor
        // is synchronous, and a disk failure must never delay or
        // break app startup. When the load completes, the holder
        // is populated and any already-open project tab refreshes
        // through the holder's Changed event.
        _ = LoadExclusionsAsync();
    }

    /// <summary>The open project tabs, in order.</summary>
    public ObservableCollection<ProjectTabViewModel> Tabs { get; } = new();

    /// <summary>The currently active tab, or null when none is open.</summary>
    public ProjectTabViewModel? ActiveTab
    {
        get => _activeTab;
        set
        {
            if (SetProperty(ref _activeTab, value))
            {
                OnPropertyChanged(nameof(HasActiveTab));
                OnPropertyChanged(nameof(ShowEmptyState));
            }
        }
    }

    /// <summary>True when at least one project is open.</summary>
    public bool HasActiveTab => ActiveTab is not null;

    /// <summary>True when no project is open (the empty state is shown).</summary>
    public bool ShowEmptyState => ActiveTab is null;

    /// <summary>A transient status message for the page.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Closes a project tab.</summary>
    public ICommand CloseTabCommand { get; }

    /// <summary>Activates a project tab when its header is clicked.</summary>
    public ICommand SelectTabCommand { get; }

    /// <summary>Opens the Manage Exclusions dialog (raised to the page code-behind).</summary>
    public ICommand ManageExclusionsCommand { get; }

    /// <summary>The current saved exclusion rules, used by the dialog to seed its working copy.</summary>
    public GlobalExclusions CurrentExclusions => _exclusionsHolder?.Current ?? GlobalExclusions.Empty;

    /// <summary>Raised when the Manage Exclusions command is invoked.</summary>
    public event EventHandler? RequestManageExclusions;

    /// <summary>
    /// Persists the new exclusion rules and broadcasts them to all open tabs
    /// through the injected <see cref="GlobalExclusionsHolder"/>. Persistence
    /// failure does not prevent the in-memory apply (the feature still works
    /// for the session; the user is told through the existing log sink).
    /// </summary>
    public async System.Threading.Tasks.Task ApplyExclusionsAsync(GlobalExclusions rules, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (_exclusionStore is not null)
        {
            try
            {
                await _exclusionStore.SaveAsync(rules, ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Persistence failure must never block the in-memory apply.
            }
        }

        _exclusionsHolder?.Replace(rules);
    }

    /// <summary>
    /// Loads the persisted global exclusion rules once at
    /// startup and populates the holder so they survive
    /// application restarts. Failure to load is silently
    /// tolerated: the store's LoadAsync never throws (it
    /// returns Empty on any failure), and any exception here
    /// is caught so startup is never blocked.
    /// </summary>
    private async System.Threading.Tasks.Task LoadExclusionsAsync()
    {
        if (_exclusionStore is null || _exclusionsHolder is null)
        {
            return;
        }

        try
        {
            var loaded = await _exclusionStore.LoadAsync().ConfigureAwait(false);
            _exclusionsHolder.Replace(loaded);
        }
        catch (Exception)
        {
            // A load failure must never break startup; the session
            // continues with the current (empty or previously applied)
            // rule set.
        }
    }

    /// <summary>
    /// Opens a local project folder as a new tab, or activates the
    /// existing tab if the project is already open. Called by the page
    /// code-behind after the folder picker returns a path.
    /// </summary>
    public void OpenProject(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        var normalized = ProjectPathNormalizer.NormalizeForDisplay(projectPath);

        // Reuse an existing tab for the same folder.
        foreach (var tab in Tabs)
        {
            if (string.Equals(tab.ProjectPath, normalized, StringComparison.OrdinalIgnoreCase))
            {
                ActiveTab = tab;
                return;
            }
        }

        var newTab = new ProjectTabViewModel(
            normalized, _enumerator, _reader, _stateStore, _copyToClipboard,
            exclusionsHolder: _exclusionsHolder);
        Tabs.Add(newTab);
        ActiveTab = newTab;
        StatusText = string.Empty;

        // Restore any saved workspace (targeted re-enumeration only,
        // never a full scan), then count this open.
        _ = RestoreTabAsync(newTab);
    }

    private static async System.Threading.Tasks.Task RestoreTabAsync(ProjectTabViewModel tab)
    {
        try
        {
            await tab.RestoreAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A restore failure must never prevent the project from opening.
        }
    }

    /// <summary>Closes a tab and activates a neighbour.</summary>
    public void CloseTab(ProjectTabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        var index = Tabs.IndexOf(tab);

        // Flush state before the tab goes away.
        _ = tab.SaveAsync();

        Tabs.Remove(tab);
        tab.Dispose();

        if (ReferenceEquals(ActiveTab, tab))
        {
            ActiveTab = Tabs.Count == 0
                ? null
                : Tabs[Math.Min(index, Tabs.Count - 1)];
        }
    }
}
