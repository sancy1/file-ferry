// ============================================================================
// FILE:     src\FileHarvester.App/ViewModels/ResolvedFileRowViewModel.cs
// PURPOSE:  View model for one row in the Resolved Files panel. Carries the
//           full list of matches for the request so the ambiguity dialog
//           can show every option.
// RELATION: Created by ResolvedFilesPanelViewModel. Consumed by the panel
//           and by MainWindow when opening the ambiguity dialog.
// ============================================================================

using Avalonia.Media;
using FileHarvester.Core.Domain;

namespace FileHarvester.App.ViewModels;

public sealed class ResolvedFileRowViewModel : ViewModelBase
{
    private FileStatus _status = FileStatus.Pending;
    private ResolvedFile? _resolved;
    private string? _note;
    private IReadOnlyList<ResolvedFile> _allMatches = Array.Empty<ResolvedFile>();

    public ResolvedFileRowViewModel(FileRequest request)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
    }

    public FileRequest Request { get; }
    public string RawInput => Request.RawInput;

    /// <summary>
    /// Every match the resolver found for this request. Populated during
    /// resolution. Used by the ambiguity dialog.
    /// </summary>
    public IReadOnlyList<ResolvedFile> AllMatches
    {
        get => _allMatches;
        set
        {
            if (SetProperty(ref _allMatches, value))
            {
                OnPropertyChanged(nameof(MatchCount));
            }
        }
    }

    public int MatchCount => _allMatches.Count;

    public FileStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusLabel));
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(StatusTextBrush));
            }
        }
    }

    public ResolvedFile? Resolved
    {
        get => _resolved;
        set
        {
            if (SetProperty(ref _resolved, value))
            {
                OnPropertyChanged(nameof(ResolvedPath));
                OnPropertyChanged(nameof(SizeText));
                OnPropertyChanged(nameof(ConfidenceText));
            }
        }
    }

    public string? Note
    {
        get => _note;
        set => SetProperty(ref _note, value);
    }

    public string ResolvedPath => _resolved?.AbsolutePath ?? Request.RawInput;

    public string StatusLabel => _status switch
    {
        FileStatus.Pending   => "Pending",
        FileStatus.Found     => "Found",
        FileStatus.NotFound  => "Not found",
        FileStatus.Ambiguous => "Ambiguous",
        FileStatus.ReadError => "Read error",
        FileStatus.Skipped   => "Skipped",
        _                    => "Unknown",
    };

    public string SizeText => _resolved is null
        ? string.Empty
        : FormatBytes(_resolved.SizeBytes);

    public string ConfidenceText => _resolved?.Confidence.ToString() ?? string.Empty;

    public IBrush StatusBrush => _status switch
    {
        FileStatus.Found     => new SolidColorBrush(Color.Parse("#065F46")),
        FileStatus.Ambiguous => new SolidColorBrush(Color.Parse("#92400E")),
        FileStatus.NotFound  => new SolidColorBrush(Color.Parse("#991B1B")),
        FileStatus.ReadError => new SolidColorBrush(Color.Parse("#991B1B")),
        FileStatus.Skipped   => new SolidColorBrush(Color.Parse("#4B5563")),
        _                    => new SolidColorBrush(Color.Parse("#3F3F46")),
    };

    public IBrush StatusTextBrush => _status switch
    {
        FileStatus.Found     => new SolidColorBrush(Color.Parse("#D1FAE5")),
        FileStatus.Ambiguous => new SolidColorBrush(Color.Parse("#FEF3C7")),
        FileStatus.NotFound  => new SolidColorBrush(Color.Parse("#FEE2E2")),
        FileStatus.ReadError => new SolidColorBrush(Color.Parse("#FEE2E2")),
        FileStatus.Skipped   => new SolidColorBrush(Color.Parse("#F3F4F6")),
        _                    => new SolidColorBrush(Color.Parse("#F4F4F5")),
    };

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
