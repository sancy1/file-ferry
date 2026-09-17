// ============================================================================
// FILE:     src\FileHarvester.App/ViewModels/ResolvedFilesPanelViewModel.cs
// PURPOSE:  View model for the Resolved Files panel. Raises ResolutionCompleted
//           after every resolve so the main window can feed the found files
//           into the Output Options panel.
// RELATION: Consumed by Views/ResolvedFilesPanel.axaml.
// ============================================================================

using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using FileHarvester.Core.Domain;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Correlation;

namespace FileHarvester.App.ViewModels;

public sealed class ResolvedFilesPanelViewModel : ViewModelBase, IDisposable
{
    private readonly IFileResolver _resolver;
    private bool _isResolving;
    private string _summaryText = "No files to resolve yet.";
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public ResolvedFilesPanelViewModel(IFileResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

        ResolveCommand = new RelayCommand(
            () => _ = RunResolveAsync(),
            () => !_isResolving && Rows.Count > 0);

        ClearCommand = new RelayCommand(
            Clear,
            () => Rows.Count > 0);

        CancelCommand = new RelayCommand(
            Cancel,
            () => _isResolving);
    }

    public event EventHandler<ResolvedFileRowViewModel>? AmbiguityRequested;

    /// <summary>
    /// Raised after every resolve job completes (success or cancel).
    /// Carries the current list of found files so listeners can react.
    /// </summary>
    public event EventHandler<IReadOnlyList<ResolvedFile>>? ResolutionCompleted;

    public ObservableCollection<ResolvedFileRowViewModel> Rows { get; } = new();

    public bool IsResolving
    {
        get => _isResolving;
        private set
        {
            if (SetProperty(ref _isResolving, value))
            {
                ((RelayCommand)ResolveCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public ICommand ResolveCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand CancelCommand { get; }

    /// <summary>
    /// The currently resolved files (Found only). Read by the main window
    /// when the harvest pipeline needs a list.
    /// </summary>
    public IReadOnlyList<ResolvedFile> CurrentResolvedFiles =>
        Rows
            .Where(r => r.Status == FileStatus.Found && r.Resolved is not null)
            .Select(r => r.Resolved!)
            .ToList();

    public void RequestAmbiguityResolution(ResolvedFileRowViewModel row)
    {
        if (row.Status == FileStatus.Ambiguous)
        {
            AmbiguityRequested?.Invoke(this, row);
        }
    }

    public void SetRequests(IReadOnlyList<FileRequest> requests)
    {
        Rows.Clear();

        foreach (var request in requests)
        {
            Rows.Add(new ResolvedFileRowViewModel(request));
        }

        NotifyRowCollectionChanged();
        SummaryText = Rows.Count == 0
            ? "No files to resolve yet."
            : $"{Rows.Count} file(s) ready. Click Resolve.";

        // Notify even on SetRequests so the output panel resets to empty.
        ResolutionCompleted?.Invoke(this, Array.Empty<ResolvedFile>());
    }

    private async Task RunResolveAsync()
    {
        if (IsResolving || Rows.Count == 0) return;

        IsResolving = true;
        _cts = new CancellationTokenSource();

        var jobCorrelationId = CorrelationContext.NewId();

        try
        {
            using (CorrelationContext.Push(jobCorrelationId))
            {
                var requests = Rows.Select(r => r.Request).ToList();
                var progress = new Progress<ResolveProgress>(OnProgress);

                var resolved = await Task.Run(
                    () => _resolver.ResolveAsync(requests, SearchOptions.Default, progress, _cts.Token),
                    _cts.Token);

                FinalizeRows(resolved);
            }
        }
        catch (OperationCanceledException) { SummaryText = "Cancelled."; }
        catch (Exception ex) { SummaryText = $"Resolve failed: {ex.Message}"; }
        finally
        {
            IsResolving = false;
            _cts?.Dispose();
            _cts = null;

            // Notify listeners with the current resolved files.
            ResolutionCompleted?.Invoke(this, CurrentResolvedFiles);
        }
    }

    private void OnProgress(ResolveProgress progress)
    {
        if (progress.Index < 0 || progress.Index >= Rows.Count) return;

        var row = Rows[progress.Index];
        row.Status = progress.Status;
        row.Note = progress.Status switch
        {
            FileStatus.Found     => "Found",
            FileStatus.NotFound  => "Not found",
            FileStatus.Ambiguous => "Multiple matches — double-click to choose",
            _                    => null,
        };
        SummaryText = $"Resolving... {progress.Processed} of {progress.Total}";
    }

    private void FinalizeRows(IReadOnlyList<ResolvedFile> resolved)
    {
        var byIndex = resolved
            .GroupBy(r => r.Request.Index)
            .ToDictionary(g => g.Key, g => g.ToList());

        for (var i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];

            if (byIndex.TryGetValue(i, out var matches))
            {
                row.AllMatches = matches;

                if (matches.Count == 1)
                {
                    row.Resolved = matches[0];
                    row.Status = FileStatus.Found;
                    row.Note = null;
                }
                else
                {
                    row.Resolved = matches[0];
                    row.Status = FileStatus.Ambiguous;
                    row.Note = $"{matches.Count} matches — double-click to choose";
                }
            }
            else
            {
                row.Status = FileStatus.NotFound;
                row.Note = "Not found";
            }
        }

        var found = Rows.Count(r => r.Status == FileStatus.Found);
        var ambiguous = Rows.Count(r => r.Status == FileStatus.Ambiguous);
        var notFound = Rows.Count(r => r.Status == FileStatus.NotFound);

        SummaryText = $"Resolved {found} of {Rows.Count}" +
            (ambiguous > 0 ? $" · {ambiguous} ambiguous" : string.Empty) +
            (notFound > 0 ? $" · {notFound} not found" : string.Empty);
    }

    private void Cancel() => _cts?.Cancel();

    private void Clear()
    {
        Rows.Clear();
        NotifyRowCollectionChanged();
        SummaryText = "No files to resolve yet.";
        ResolutionCompleted?.Invoke(this, Array.Empty<ResolvedFile>());
    }

    private void NotifyRowCollectionChanged()
    {
        OnPropertyChanged(nameof(Rows));
        ((RelayCommand)ResolveCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ClearCommand).RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _cts?.Dispose();
        _cts = null;
        _disposed = true;
    }
}
