// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/ProjectNodeRowViewModel.cs
// PURPOSE:  View model for one visible row in the CodeViewer Project
//           Browser: indentation, expand/collapse arrow, tri-state
//           checkbox, folder/file icon, and the name label. Wraps a
//           ProjectNode and delegates expand/check to the owning
//           ProjectTabViewModel so the single selection engine stays the
//           source of truth.
// RELATION: Produced by ProjectTabViewModel.VisibleRows. Bound by the
//           Project Browser item template in CodeViewerPage.axaml.
// ============================================================================

using System.Windows.Input;
using CodeViewer.Core.Domain;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// One visible row in the Project Browser.
/// </summary>
public sealed class ProjectNodeRowViewModel : ViewModelBase
{
    private readonly ProjectTabViewModel _owner;
    private bool _isSelectedFile;

    public ProjectNodeRowViewModel(ProjectNode node, ProjectTabViewModel owner)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));

        ToggleExpandCommand = new RelayCommand(
            () => _owner.ToggleExpand(Node),
            () => Node.Kind == NodeKind.Directory);
        ToggleCheckCommand = new RelayCommand(() => _owner.ToggleCheck(Node));
        SelectCommand = new RelayCommand(
            () => _owner.SelectFile(Node),
            () => Node.Kind == NodeKind.File);
    }

    /// <summary>The underlying domain node.</summary>
    public ProjectNode Node { get; }

    /// <summary>The display name.</summary>
    public string Name => Node.Name;

    /// <summary>Horizontal indent in pixels, from the node's depth.</summary>
    public double Indent => Node.Depth * 20.0;

    /// <summary>True for directories (shows the expand/collapse arrow).</summary>
    public bool IsDirectory => Node.Kind == NodeKind.Directory;

    /// <summary>True for files (shows the file icon and is selectable).</summary>
    public bool IsFile => Node.Kind == NodeKind.File;

    /// <summary>Whether the directory is expanded.</summary>
    public bool IsExpanded => Node.IsExpanded;

    /// <summary>Whether the directory's children are being enumerated.</summary>
    public bool IsLoading => Node.LoadState == NodeLoadState.Loading;

    /// <summary>
    /// The tri-state checkbox value: true = checked, false = unchecked,
    /// null = partial (some descendants checked).
    /// </summary>
    public bool? IsChecked => Node.Selection switch
    {
        NodeSelectionState.Checked => true,
        NodeSelectionState.Partial => null,
        _ => false,
    };

    /// <summary>True when this file is the currently selected code card.</summary>
    public bool IsSelectedFile
    {
        get => _isSelectedFile;
        internal set => SetProperty(ref _isSelectedFile, value);
    }

    /// <summary>Expands/collapses a directory (loads children on first expand).</summary>
    public ICommand ToggleExpandCommand { get; }

    /// <summary>Checks/unchecks the node (recursive for directories).</summary>
    public ICommand ToggleCheckCommand { get; }

    /// <summary>Selects a file as the active code card.</summary>
    public ICommand SelectCommand { get; }

    /// <summary>Refreshes the derived properties after the node changes.</summary>
    internal void Refresh()
    {
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsChecked));
    }
}
