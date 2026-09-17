// ============================================================================
// FILE:     src\FileHarvester.App/ViewModels/InputPanelViewModel.cs
// PURPOSE:  View model for the Input Intake panel. Holds pasted paths and
//           the collected FileRequest list. Search mode is not exposed;
//           the resolver always uses universal matching.
// RELATION: Consumed by Views/InputPanel.axaml.
// ============================================================================

using System.Collections.ObjectModel;
using System.Windows.Input;
using FileHarvester.Core.Domain;

namespace FileHarvester.App.ViewModels;

public sealed class InputPanelViewModel : ViewModelBase
{
    private static readonly char[] LineSeparators = { '\r', '\n' };

    private string _pasteText = string.Empty;
    private string _statusText = "No files yet. Paste paths, add files, or drag and drop.";

    public InputPanelViewModel()
    {
        ClearAllCommand = new RelayCommand(ClearAll, () => Files.Count > 0);
        RemoveFileCommand = new RelayCommand<FileRequest>(RemoveFile);
    }

    public event EventHandler? FilesChanged;

    public string PasteText
    {
        get => _pasteText;
        set => SetProperty(ref _pasteText, value);
    }

    public ObservableCollection<FileRequest> Files { get; } = new();

    public int FileCount => Files.Count;
    public bool HasFiles => Files.Count > 0;

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public ICommand ClearAllCommand { get; }
    public ICommand RemoveFileCommand { get; }

    public int AddFilesFromPaths(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var added = 0;

        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var trimmed = raw.Trim();

            var exists = Files.Any(f =>
                string.Equals(f.RawInput, trimmed, StringComparison.OrdinalIgnoreCase));

            if (exists) continue;

            Files.Add(new FileRequest(Files.Count, trimmed));
            added++;
        }

        if (added > 0) NotifyCollectionChanged();
        return added;
    }

    public int ParsePasteBox()
    {
        var lines = PasteText.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
        var added = AddFilesFromPaths(lines);

        if (added > 0)
        {
            PasteText = string.Empty;
            UpdateStatus($"Added {added} new file(s).");
        }
        else if (lines.Length > 0)
        {
            UpdateStatus("No new files added (all duplicates or empty).");
        }

        return added;
    }

    private void ClearAll()
    {
        Files.Clear();
        PasteText = string.Empty;
        StatusText = "Cleared.";
        NotifyCollectionChanged();
    }

    private void RemoveFile(FileRequest? file)
    {
        if (file is null) return;
        if (Files.Remove(file)) NotifyCollectionChanged();
    }

    private void UpdateStatus(string message) => StatusText = message;

    private void NotifyCollectionChanged()
    {
        OnPropertyChanged(nameof(FileCount));
        OnPropertyChanged(nameof(HasFiles));
        ((RelayCommand)ClearAllCommand).RaiseCanExecuteChanged();
        FilesChanged?.Invoke(this, EventArgs.Empty);
    }
}
