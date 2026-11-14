// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/ProjectTabViewModel.cs
// PURPOSE:  The workspace for one open project tab. Owns the project's
//           ProjectTreeState (the single selection engine), drives lazy
//           directory loading on expand, orchestrates Auto Display with a
//           per-tab cancellation token, and exposes the three projections
//           the panes bind to: browser rows, structure text, code cards.
//           All state is per-tab; switching tabs never leaks selections.
// RELATION: Owned by CodeViewerViewModel. Consumes ProjectTreeEnumerator,
//           ProjectFileReader, and IWorkspaceStateStore. Bound by
//           CodeViewerPage.axaml.
// ============================================================================

using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using CodeViewer.Core.Domain;
using CodeViewer.Core.Tree;
using CodeViewer.Platform.FileSystem;
using CodeViewer.Platform.State;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The workspace view model for one open project.
/// </summary>
public sealed class ProjectTabViewModel : ViewModelBase, IDisposable
{
    private readonly ProjectTreeEnumerator _enumerator;
    private readonly ProjectFileReader _reader;
    private readonly IWorkspaceStateStore _stateStore;
    private readonly Func<string, System.Threading.Tasks.Task> _copyToClipboard;
    private readonly IUiDispatcher _ui;
    private readonly GlobalExclusionsHolder? _exclusionsHolder;

    private CancellationTokenSource? _autoDisplayCts;
    private CancellationTokenSource? _saveDebounceCts;
    private string _structureText = string.Empty;
    private int _structureLineCount;
    private string _statusText = string.Empty;
    private bool _isAutoDisplayBusy;
    private int _openedCount = 1;

    // Pane dimensions and scroll positions, captured for persistence.
    // The view writes these; the store reads them on save.
    private double _browserScrollOffset;
    private double _structureScrollOffset;
    private double _codeScrollOffset;
    private double _browserWidth;
    private double _structureRatio;

    public ProjectTabViewModel(
        string projectPath,
        ProjectTreeEnumerator enumerator,
        ProjectFileReader reader,
        IWorkspaceStateStore stateStore,
        Func<string, System.Threading.Tasks.Task> copyToClipboard,
        IUiDispatcher? uiDispatcher = null,
        GlobalExclusionsHolder? exclusionsHolder = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        _enumerator = enumerator ?? throw new ArgumentNullException(nameof(enumerator));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _copyToClipboard = copyToClipboard ?? throw new ArgumentNullException(nameof(copyToClipboard));
        _ui = uiDispatcher ?? new AvaloniaUiDispatcher();
        _exclusionsHolder = exclusionsHolder;
        if (_exclusionsHolder is not null)
        {
            _exclusionsHolder.Changed += OnGlobalExclusionsChanged;
        }

        ProjectPath = ProjectPathNormalizer.NormalizeForDisplay(projectPath);
        State = new ProjectTreeState(
            Path.GetFileName(ProjectPath.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name
                ? name
                : ProjectPath,
            ProjectPath);

        // The root row is shown immediately, collapsed and unchecked.
        BrowserRows.Add(new ProjectNodeRowViewModel(State.Root, this));

        // Aggressively enabled: RelayCommand does not observe
        // StructureLineCount changes, so the previous predicate
        // (StructureLineCount > 0) left the Copy Tree button stuck
        // disabled after a project loaded. Copying an empty
        // StructureText is safe; the button must never be dead.
        CopyTreeCommand = new RelayCommand(
            () => _ = _copyToClipboard(StructureText),
            () => true);
        // Aggressively enabled: same reason as CopyTreeCommand. The
        // previous (CodeCards.Count > 0) predicate left Copy All stuck
        // disabled. CombineAllCode() safely returns empty for zero cards.
        CopyAllCommand = new RelayCommand(
            () => _ = _copyToClipboard(CombineAllCode()),
            () => true);
        ToggleAutoDisplayCommand = new AsyncRelayCommand(ToggleAutoDisplayAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    /// <summary>The single selection engine for this project.</summary>
    public ProjectTreeState State { get; }

    /// <summary>The normalized project root path shown under the name.</summary>
    public string ProjectPath { get; }

    /// <summary>The project display name.</summary>
    public string ProjectName => State.ProjectName;

    /// <summary>The visible browser rows (flattened, expansion-aware).</summary>
    public ObservableCollection<ProjectNodeRowViewModel> BrowserRows { get; } = new();

    /// <summary>The code cards for checked files.</summary>
    public ObservableCollection<CodeFileCardViewModel> CodeCards { get; } = new();

    /// <summary>How many times the project has been opened.</summary>
    public int OpenedCount
    {
        get => _openedCount;
        private set
        {
            if (SetProperty(ref _openedCount, value))
            {
                OnPropertyChanged(nameof(OpenedLabel));
            }
        }
    }

    /// <summary>The "Opened: N times" label from the reference design.</summary>
    public string OpenedLabel => $"Opened: {OpenedCount} time{(OpenedCount == 1 ? "" : "s")}";

    /// <summary>The rendered Project Tree Structure text.</summary>
    public string StructureText
    {
        get => _structureText;
        private set => SetProperty(ref _structureText, value);
    }

    /// <summary>The structure line count shown in the pane header.</summary>
    public int StructureLineCount
    {
        get => _structureLineCount;
        private set
        {
            if (SetProperty(ref _structureLineCount, value))
            {
                OnPropertyChanged(nameof(StructureLineCountLabel));
            }
        }
    }

    /// <summary>The "105 lines" label.</summary>
    public string StructureLineCountLabel => $"{StructureLineCount} lines";

    /// <summary>The "11 files visible" label for the Code Content header.</summary>
    public string CodeFileCountLabel => $"{CodeCards.Count} file{(CodeCards.Count == 1 ? "" : "s")} visible";

    /// <summary>Whether Auto Display is on.</summary>
    public bool AutoDisplay => State.AutoDisplay;

    /// <summary>True while an Auto Display scan is running.</summary>
    public bool IsAutoDisplayBusy
    {
        get => _isAutoDisplayBusy;
        private set => SetProperty(ref _isAutoDisplayBusy, value);
    }

    /// <summary>A transient status message for the workspace.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>The active display mode.</summary>
    public DisplayMode DisplayMode
    {
        get => State.DisplayMode;
        set
        {
            if (State.DisplayMode != value)
            {
                State.DisplayMode = value;
                OnPropertyChanged();
                ScheduleSave();
            }
        }
    }

    /// <summary>The per-project "Show system files" toggle.</summary>
    public bool ShowSystemFiles { get; private set; }

    // Pane dimension / scroll properties the view binds to (TwoWay) so
    // they persist per project.
    public double BrowserScrollOffset { get => _browserScrollOffset; set => SetProperty(ref _browserScrollOffset, value); }
    public double StructureScrollOffset { get => _structureScrollOffset; set => SetProperty(ref _structureScrollOffset, value); }
    public double CodeScrollOffset { get => _codeScrollOffset; set => SetProperty(ref _codeScrollOffset, value); }
    public double BrowserWidth { get => _browserWidth; set => SetProperty(ref _browserWidth, value); }
    public double StructureRatio { get => _structureRatio; set => SetProperty(ref _structureRatio, value); }

    /// <summary>
    /// Sets the "Show system files" toggle and re-enumerates loaded
    /// directories so the change takes effect immediately.
    /// </summary>
    public void SetShowSystemFiles(bool value)
    {
        if (ShowSystemFiles == value)
        {
            return;
        }

        ShowSystemFiles = value;
        OnPropertyChanged(nameof(ShowSystemFiles));
        _ = RefreshAsync();
    }

    public ICommand CopyTreeCommand { get; }
    public ICommand CopyAllCommand { get; }
    public ICommand ToggleAutoDisplayCommand { get; }
    public ICommand RefreshCommand { get; }

    // ------------------------------------------------------------------
    // Expansion (lazy load on first expand)
    // ------------------------------------------------------------------

    /// <summary>
    /// Expands or collapses a directory, loading children on first expand.
    /// Returns a task that completes when any lazy load finishes, so tests
    /// can await it; production callers (commands) may ignore it.
    /// </summary>
    public System.Threading.Tasks.Task ToggleExpand(ProjectNode node)
    {
        if (node.Kind != NodeKind.Directory)
        {
            return System.Threading.Tasks.Task.CompletedTask;
        }

        System.Threading.Tasks.Task load = System.Threading.Tasks.Task.CompletedTask;
        if (node.IsExpanded)
        {
            State.Collapse(node);
        }
        else
        {
            State.Expand(node);
            if (node.LoadState is NodeLoadState.NotLoaded or NodeLoadState.Failed)
            {
                load = LoadChildrenAsync(node);
            }
        }

        RebuildBrowserRows();
        ScheduleSave();
        return load;
    }

    private async System.Threading.Tasks.Task LoadChildrenAsync(ProjectNode directory)
    {
        State.MarkLoading(directory);
        RefreshRow(directory);
        try
        {
            var children = new List<ProjectNode>();
            await foreach (var child in _enumerator.EnumerateChildrenAsync(
                               directory, ShowSystemFiles, exclusions: _exclusionsHolder?.Current).ConfigureAwait(false))
            {
                children.Add(child);
            }

            await RunOnUiAsync(() =>
            {
                State.SetChildren(directory, children);
                RebuildBrowserRows();
                RefreshProjections();
            });
        }
        catch (OperationCanceledException)
        {
            await RunOnUiAsync(() =>
            {
                State.MarkFailed(directory);
                RefreshRow(directory);
            });
        }
    }

    // ------------------------------------------------------------------
    // Selection
    // ------------------------------------------------------------------

    /// <summary>Checks/unchecks a node (recursive for directories).</summary>
    public void ToggleCheck(ProjectNode node)
    {
        State.ToggleChecked(node);
        RebuildBrowserRows();
        RefreshProjections();
        ScheduleSave();
    }

    /// <summary>Selects a file as the active code card.</summary>
    public void SelectFile(ProjectNode node)
    {
        if (node.Kind != NodeKind.File)
        {
            return;
        }

        State.SelectedFile = node;
        foreach (var row in BrowserRows)
        {
            row.IsSelectedFile = ReferenceEquals(row.Node, node);
        }

        ScheduleSave();
    }

    // ------------------------------------------------------------------
    // Auto Display
    // ------------------------------------------------------------------

    private async System.Threading.Tasks.Task ToggleAutoDisplayAsync()
    {
        if (State.AutoDisplay)
        {
            // OFF: cancel any running scan, uncheck all, clear viewers,
            // preserve expansion.
            _autoDisplayCts?.Cancel();
            State.SetAutoDisplayOff();
            OnPropertyChanged(nameof(AutoDisplay));
            RebuildBrowserRows();
            RefreshProjections();
            StatusText = string.Empty;
            ScheduleSave();
            return;
        }

        // ON: full asynchronous, cancellable walk.
        _autoDisplayCts?.Cancel();
        _autoDisplayCts = new CancellationTokenSource();
        var ct = _autoDisplayCts.Token;

        IsAutoDisplayBusy = true;
        StatusText = "Scanning project…";
        try
        {
            await AutoDisplayWalkAsync(ct).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                State.SetAutoDisplayOn();
                OnPropertyChanged(nameof(AutoDisplay));
                RebuildBrowserRows();
                RefreshProjections();
                StatusText = string.Empty;
                ScheduleSave();
            });
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = string.Empty);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsAutoDisplayBusy = false);
        }
    }

    // Depth-first walk that lazily loads every directory, so the whole
    // tree is materialized before SetAutoDisplayOn checks and expands it.
    private async System.Threading.Tasks.Task AutoDisplayWalkAsync(CancellationToken ct)
    {
        var pending = new Stack<ProjectNode>();
        pending.Push(State.Root);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            if (directory.LoadState != NodeLoadState.Loaded)
            {
                var children = new List<ProjectNode>();
                await foreach (var child in _enumerator.EnumerateChildrenAsync(
                                   directory, ShowSystemFiles, exclusions: _exclusionsHolder?.Current, cancellationToken: ct).ConfigureAwait(false))
                {
                    children.Add(child);
                }

                var dir = directory;
                await Dispatcher.UIThread.InvokeAsync(() =>
                    State.SetChildren(dir, children));
            }

            foreach (var child in directory.Children)
            {
                if (child.Kind == NodeKind.Directory)
                {
                    pending.Push(child);
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // Refresh (re-enumerate loaded directories)
    // ------------------------------------------------------------------

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        // Re-enumerate every currently loaded directory, preserving
        // expansion and selection where the entries still exist.
        var loaded = new List<ProjectNode>();
        CollectLoadedDirectories(State.Root, loaded);

        foreach (var directory in loaded)
        {
            var children = new List<ProjectNode>();
            await foreach (var child in _enumerator.EnumerateChildrenAsync(
                               directory, ShowSystemFiles, exclusions: _exclusionsHolder?.Current).ConfigureAwait(false))
            {
                children.Add(child);
            }

            var dir = directory;
            await Dispatcher.UIThread.InvokeAsync(() =>
                State.SetChildren(dir, children));
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            RebuildBrowserRows();
            RefreshProjections();
        });
    }

    private static void CollectLoadedDirectories(ProjectNode node, List<ProjectNode> into)
    {
        if (node.Kind == NodeKind.Directory && node.LoadState == NodeLoadState.Loaded)
        {
            into.Add(node);
        }

        foreach (var child in node.Children)
        {
            CollectLoadedDirectories(child, into);
        }
    }

    // ------------------------------------------------------------------
    // Projections
    // ------------------------------------------------------------------

    private void RebuildBrowserRows()
    {
        var selectedFile = State.SelectedFile;
        var rows = State.GetVisibleRows();

        // Rebuild in place so the virtualized panel reuses containers.
        BrowserRows.Clear();
        foreach (var node in rows)
        {
            BrowserRows.Add(new ProjectNodeRowViewModel(node, this)
            {
                IsSelectedFile = ReferenceEquals(node, selectedFile),
            });
        }
    }

    private void RefreshProjections()
    {
        StructureText = StructureTreeRenderer.Render(State.Root);
        StructureLineCount = StructureTreeRenderer.CountLines(State.Root);

        CodeCards.Clear();
        foreach (var file in State.GetSelectedFiles())
        {
            CodeCards.Add(new CodeFileCardViewModel(file, _reader,
                text => _ = _copyToClipboard(text)));
        }

        OnPropertyChanged(nameof(CodeFileCountLabel));
    }

    private void RefreshRow(ProjectNode node)
    {
        foreach (var row in BrowserRows)
        {
            if (ReferenceEquals(row.Node, node))
            {
                row.Refresh();
                break;
            }
        }
    }

    private string CombineAllCode()
    {
        var builder = new System.Text.StringBuilder();
        foreach (var card in CodeCards)
        {
            if (card.HasPreview)
            {
                // Heading ends with a newline, so AppendLine adds the blank
                // line that separates the heading from the body.
                builder.AppendLine(card.Heading);
                builder.AppendLine(card.Text);
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    // ------------------------------------------------------------------
    // Workspace persistence (D4: restore without a full scan)
    // ------------------------------------------------------------------

    /// <summary>
    /// Restores a previously saved workspace. Enumerates only the
    /// directories needed to reconstruct the saved expanded/selected
    /// paths; never performs a full recursive scan. Newly discovered
    /// files appear unchecked. Missing paths are dropped.
    /// </summary>
    public async System.Threading.Tasks.Task RestoreAsync(CancellationToken ct = default)
    {
        WorkspaceState? saved;
        try
        {
            saved = await _stateStore.LoadAsync(ProjectPath, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            saved = null; // persistence failure must never block opening
        }

        if (saved is null)
        {
            return;
        }

        OpenedCount = Math.Max(saved.OpenedCount, 1);
        ShowSystemFiles = saved.ShowSystemFiles;
        DisplayMode = saved.DisplayMode;
        BrowserScrollOffset = saved.BrowserScrollOffset;
        StructureScrollOffset = saved.StructureScrollOffset;
        CodeScrollOffset = saved.CodeScrollOffset;
        BrowserWidth = saved.BrowserWidth;
        StructureRatio = saved.StructureRatio;

        // Re-enumerate only the saved expanded directories, deepest-first
        // so parents exist before their children are attached.
        var expandedDirs = saved.ExpandedDirectories
            .OrderBy(d => d.Count(c => c == Path.DirectorySeparatorChar))
            .ToList();

        foreach (var relativeDir in expandedDirs)
        {
            ct.ThrowIfCancellationRequested();
            var node = FindNodeByRelativePath(relativeDir);
            if (node is null || node.Kind != NodeKind.Directory)
            {
                continue;
            }

            await LoadChildrenForRestoreAsync(node, ct).ConfigureAwait(false);
            await RunOnUiAsync(() => State.Expand(node));
        }

        // Re-apply saved selections by path. Newly discovered files that
        // were not saved stay unchecked.
        foreach (var relativePath in saved.CheckedPaths)
        {
            ct.ThrowIfCancellationRequested();
            var node = FindNodeByRelativePath(relativePath);
            if (node is not null)
            {
                await RunOnUiAsync(() => State.SetChecked(node, true));
            }
        }

        // Restore the selected file.
        if (saved.SelectedFilePath is not null)
        {
            var selected = FindNodeByRelativePath(saved.SelectedFilePath);
            if (selected is not null)
            {
                await RunOnUiAsync(() => SelectFile(selected));
            }
        }

        // Restore Auto Display flag WITHOUT triggering a fresh scan: the
        // saved selections already reflect what Auto Display had checked.
        if (saved.AutoDisplay)
        {
            await RunOnUiAsync(() => State.RestoreAutoDisplayFlag());
        }

        await RunOnUiAsync(() =>
        {
            RebuildBrowserRows();
            RefreshProjections();
        });
    }

    // Runs an action on the UI thread via the injected dispatcher.
    // Production uses the Avalonia dispatcher; tests use an inline
    // dispatcher so async methods can be awaited without a running app.
    private System.Threading.Tasks.Task RunOnUiAsync(Action action) => _ui.RunAsync(action);

    // Enumerate one directory's children during restore (no recursion
    // beyond the saved expanded set).
    private async System.Threading.Tasks.Task LoadChildrenForRestoreAsync(
        ProjectNode directory, CancellationToken ct)
    {
        var children = new List<ProjectNode>();
        await foreach (var child in _enumerator.EnumerateChildrenAsync(
                           directory, ShowSystemFiles, exclusions: _exclusionsHolder?.Current, cancellationToken: ct).ConfigureAwait(false))
        {
            children.Add(child);
        }

        await RunOnUiAsync(() => State.SetChildren(directory, children));
    }

    // Finds a node by its root-relative path, loading intermediate
    // directories is NOT done here (restore only walks saved expanded
    // dirs, which are loaded deepest-first before this is called).
    private ProjectNode? FindNodeByRelativePath(string relativePath)
    {
        var parts = relativePath.Split(
            Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = State.Root;

        foreach (var part in parts)
        {
            var next = current.Children.FirstOrDefault(
                c => string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    /// <summary>
    /// Captures the current workspace into a WorkspaceState and schedules
    /// a debounced save. Source code is never persisted - only paths and
    /// UI state.
    /// </summary>
    public void ScheduleSave()
    {
        _saveDebounceCts?.Cancel();
        _saveDebounceCts = new CancellationTokenSource();
        var ct = _saveDebounceCts.Token;

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(500, ct).ConfigureAwait(false);
                if (!ct.IsCancellationRequested)
                {
                    await SaveAsync(ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    /// <summary>Saves the current workspace state immediately.</summary>
    public async System.Threading.Tasks.Task SaveAsync(CancellationToken ct = default)
    {
        var state = new WorkspaceState
        {
            ProjectPath = ProjectPath,
            ProjectName = ProjectName,
            OpenedCount = OpenedCount,
            DisplayMode = DisplayMode,
            AutoDisplay = State.AutoDisplay,
            ShowSystemFiles = ShowSystemFiles,
            BrowserScrollOffset = BrowserScrollOffset,
            StructureScrollOffset = StructureScrollOffset,
            CodeScrollOffset = CodeScrollOffset,
            BrowserWidth = BrowserWidth,
            StructureRatio = StructureRatio,
        };

        foreach (var node in State.GetAllNodes())
        {
            if (node.Kind == NodeKind.Directory && node.IsExpanded)
            {
                state.ExpandedDirectories.Add(RelativePath(node));
            }

            if (node.Selection == NodeSelectionState.Checked)
            {
                state.CheckedPaths.Add(RelativePath(node));
            }
        }

        if (State.SelectedFile is not null)
        {
            state.SelectedFilePath = RelativePath(State.SelectedFile);
        }

        try
        {
            await _stateStore.SaveAsync(state, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Persistence failure must never disrupt the workspace.
        }
    }

    private string RelativePath(ProjectNode node)
    {
        var parts = new List<string>();
        for (var current = node; current is not null && !ReferenceEquals(current, State.Root); current = current.Parent)
        {
            parts.Insert(0, current.Name);
        }

        return string.Join(Path.DirectorySeparatorChar, parts);
    }

    private void OnGlobalExclusionsChanged(object? sender, GlobalExclusions rules)
    {
        // Rules changed elsewhere (dialog Save & Apply). Cancel any pending
        // Auto Display scan so it cannot reintroduce excluded nodes, then
        // re-enumerate every loaded directory with the new rules.
        _autoDisplayCts?.Cancel();
        _ = RunOnUiAsync(() => _ = RefreshAsync());
    }

    /// <summary>Cancels any running Auto Display scan and saves state.</summary>
    public void Dispose()
    {
        if (_exclusionsHolder is not null)
        {
            _exclusionsHolder.Changed -= OnGlobalExclusionsChanged;
        }

        _autoDisplayCts?.Cancel();
        _autoDisplayCts?.Dispose();
        _saveDebounceCts?.Cancel();
        _saveDebounceCts?.Dispose();
    }
}
