// ============================================================================
// FILE:     src\FileHarvester.App/ViewModels/AmbiguityDialogViewModel.cs
// PURPOSE:  View model for the ambiguity dialog. Shows every match for a
//           bare filename and lets the user pick which to include.
// RELATION: Created by ResolvedFilesPanelViewModel when the user
//           double-clicks an Ambiguous row. Consumed by
//           Views/AmbiguityDialog.axaml.
// ============================================================================

using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using FileHarvester.Core.Domain;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// View model for the ambiguity picker.
/// </summary>
public sealed class AmbiguityDialogViewModel : ViewModelBase
{
    private string _statusText = string.Empty;

    public AmbiguityDialogViewModel(
        string query,
        IReadOnlyList<ResolvedFile> matches)
    {
        Query = query ?? throw new ArgumentNullException(nameof(query));
        ArgumentNullException.ThrowIfNull(matches);

        foreach (var m in matches)
        {
            Rows.Add(new AmbiguityRowViewModel(m));
        }

        StatusText = $"{Rows.Count} match(es) — select the ones to include";

        SelectAllCommand = new RelayCommand(() => SetAll(true));
        SelectNoneCommand = new RelayCommand(() => SetAll(false));
        SelectNewestCommand = new RelayCommand(SelectNewest);
        SelectLargestCommand = new RelayCommand(SelectLargest);
    }

    /// <summary>
    /// The bare name the user searched for.
    /// </summary>
    public string Query { get; }

    /// <summary>
    /// All matches with their checkbox state.
    /// </summary>
    public ObservableCollection<AmbiguityRowViewModel> Rows { get; } = new();

    /// <summary>
    /// Number of rows currently checked.
    /// </summary>
    public int SelectedCount => Rows.Count(r => r.IsSelected);

    /// <summary>
    /// Status line at the bottom of the dialog.
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>
    /// The paths of the rows the user checked. Called by the window when
    /// the user confirms.
    /// </summary>
    public IReadOnlyList<ResolvedFile> SelectedMatches =>
        Rows.Where(r => r.IsSelected).Select(r => r.Match).ToList();

    public ICommand SelectAllCommand { get; }
    public ICommand SelectNoneCommand { get; }
    public ICommand SelectNewestCommand { get; }
    public ICommand SelectLargestCommand { get; }

    // ------------------------------------------------------------------

    private void SetAll(bool selected)
    {
        foreach (var row in Rows)
        {
            row.IsSelected = selected;
        }

        UpdateStatus();
    }

    private void SelectNewest()
    {
        SetAll(false);

        foreach (var row in Rows
            .OrderByDescending(r => r.Match.LastModifiedUtc)
            .Take(5))
        {
            row.IsSelected = true;
        }

        UpdateStatus();
    }

    private void SelectLargest()
    {
        SetAll(false);

        foreach (var row in Rows
            .OrderByDescending(r => r.Match.SizeBytes)
            .Take(5))
        {
            row.IsSelected = true;
        }

        UpdateStatus();
    }

    private void UpdateStatus() =>
        StatusText = $"{SelectedCount} of {Rows.Count} selected";
}

/// <summary>
/// One match in the ambiguity dialog.
/// </summary>
public sealed class AmbiguityRowViewModel : ViewModelBase
{
    private bool _isSelected;

    public AmbiguityRowViewModel(ResolvedFile match)
    {
        Match = match ?? throw new ArgumentNullException(nameof(match));
    }

    public ResolvedFile Match { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string Path => Match.AbsolutePath;

    public string SizeText => FormatBytes(Match.SizeBytes);

    public string ModifiedText =>
        Match.LastModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
