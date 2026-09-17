// ============================================================================
// FILE:     src\FileHarvester.App/ViewModels/OutputOptionsViewModel.cs
// PURPOSE:  View model for the Output Options panel. Now exposes an
//           OutputFileName field with a smart default: single-file harvest
//           uses the source base name; multi-file harvest uses "bundle".
//           The user can override.
// RELATION: Consumed by Views/OutputOptionsPanel.axaml.
// ============================================================================

using System.Collections.ObjectModel;
using System.Windows.Input;
using FileHarvester.Core.Domain;
using FileHarvester.Core.Writers;

namespace FileHarvester.App.ViewModels;

public sealed class OutputOptionsViewModel : ViewModelBase, IDisposable
{
    private readonly HarvestJobRunner _runner;
    private readonly List<ResolvedFile> _resolvedFiles = new();

    private OutputMode _selectedMode = OutputMode.SingleMerged;
    private FormatOptionViewModel? _selectedFormat;
    private string _outputFileName = string.Empty;

    private bool _twoBlankLines = true;
    private bool _includeTableOfContents;
    private bool _includeManifest;
    private bool _preserveFolderStructure = true;
    private bool _includeHeaders;
    private bool _includeCodeFences;
    private string _destinationPath = string.Empty;
    private string _statusText = "Choose a format and destination, then click Harvest.";
    private bool _isHarvesting;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public OutputOptionsViewModel(
        IFormatWriterRegistry registry,
        HarvestJobRunner runner)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));

        HarvestCommand = new RelayCommand(
            () => _ = RunHarvestAsync(),
            () => CanHarvest);

        SelectDestinationCommand = new RelayCommand(
            () => { /* code-behind */ });

        Formats = new ObservableCollection<FormatOptionViewModel>(
            registry.AllWriters.Select(w =>
                new FormatOptionViewModel(w.Extension, w.DisplayName, w.MimeType)));

        if (Formats.Count > 0)
        {
            SelectedFormat = Formats[0];
        }
    }

    public event EventHandler<SaveResult>? HarvestCompleted;

    public OutputMode SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (SetProperty(ref _selectedMode, value))
            {
                OnPropertyChanged(nameof(IsMergedMode));
                OnPropertyChanged(nameof(IsSeparateMode));
                OnPropertyChanged(nameof(ModeDescription));
            }
        }
    }

    public bool IsMergedMode
    {
        get => _selectedMode == OutputMode.SingleMerged;
        set
        {
            if (value && _selectedMode != OutputMode.SingleMerged)
                SelectedMode = OutputMode.SingleMerged;
        }
    }

    public bool IsSeparateMode
    {
        get => _selectedMode == OutputMode.SeparateFiles;
        set
        {
            if (value && _selectedMode != OutputMode.SeparateFiles)
                SelectedMode = OutputMode.SeparateFiles;
        }
    }

    public string ModeDescription => _selectedMode == OutputMode.SingleMerged
        ? "All files merged into one document."
        : "Each file written to its own output.";

    public ObservableCollection<FormatOptionViewModel> Formats { get; }

    public FormatOptionViewModel? SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (SetProperty(ref _selectedFormat, value))
            {
                OnPropertyChanged(nameof(CanHarvest));
                OnPropertyChanged(nameof(FilePreviewText));
                ((RelayCommand)HarvestCommand).RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// User-supplied name for the merged output file. Empty means the sink
    /// falls back to the first source file's base name, or "bundle".
    /// The extension is appended by the sink based on the selected format.
    /// </summary>
    public string OutputFileName
    {
        get => _outputFileName;
        set
        {
            if (SetProperty(ref _outputFileName, value))
            {
                OnPropertyChanged(nameof(FilePreviewText));
            }
        }
    }

    /// <summary>
    /// Live preview of the file that will be written: "PHCN.md", "bundle.pdf", etc.
    /// </summary>
    public string FilePreviewText
    {
        get
        {
            var ext = _selectedFormat?.Extension ?? ".?";
            var baseName = string.IsNullOrWhiteSpace(_outputFileName)
                ? "bundle"
                : _outputFileName.Trim();

            // If the user typed an extension, do not double it in the preview.
            if (baseName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                return baseName;
            }

            return baseName + ext;
        }
    }
    public bool TwoBlankLines
    {
        get => _twoBlankLines;
        set => SetProperty(ref _twoBlankLines, value);
    }

    public bool IncludeTableOfContents
    {
        get => _includeTableOfContents;
        set => SetProperty(ref _includeTableOfContents, value);
    }

    public bool IncludeManifest
    {
        get => _includeManifest;
        set => SetProperty(ref _includeManifest, value);
    }

    public bool PreserveFolderStructure
    {
        get => _preserveFolderStructure;
        set => SetProperty(ref _preserveFolderStructure, value);
    }

    public bool IncludeHeaders
    {
        get => _includeHeaders;
        set => SetProperty(ref _includeHeaders, value);
    }

    public bool IncludeCodeFences
    {
        get => _includeCodeFences;
        set => SetProperty(ref _includeCodeFences, value);
    }

    public string DestinationPath
    {
        get => _destinationPath;
        set => SetProperty(ref _destinationPath, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsHarvesting
    {
        get => _isHarvesting;
        private set
        {
            if (SetProperty(ref _isHarvesting, value))
            {
                OnPropertyChanged(nameof(CanHarvest));
                ((RelayCommand)HarvestCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanHarvest =>
        !_isHarvesting
        && _selectedFormat is not null
        && _resolvedFiles.Count > 0;

    public ICommand HarvestCommand { get; }
    public ICommand SelectDestinationCommand { get; }

    /// <summary>
    /// Called by MainWindowViewModel when the Resolved Files panel updates.
    /// Refreshes the OutputFileName default to match the current file set
    /// (unless the user has typed a custom name).
    /// </summary>
    public void SetResolvedFiles(IReadOnlyList<ResolvedFile> files)
    {
        _resolvedFiles.Clear();
        _resolvedFiles.AddRange(files);

        // Smart default: single file → its base name; multiple → "bundle";
        // zero → empty. Only auto-update if the user has not typed a
        // custom name.
        if (string.IsNullOrWhiteSpace(_outputFileName)
            || _outputFileName == "bundle"
            || IsAutoDefaultName(_outputFileName))
        {
            if (files.Count == 1)
            {
                var baseName = Path.GetFileNameWithoutExtension(
                    files[0].AbsolutePath);
                OutputFileName = baseName;
            }
            else if (files.Count > 1)
            {
                OutputFileName = "bundle";
            }
            else
            {
                OutputFileName = string.Empty;
            }
        }

        OnPropertyChanged(nameof(CanHarvest));
        OnPropertyChanged(nameof(FilePreviewText));
        ((RelayCommand)HarvestCommand).RaiseCanExecuteChanged();

        StatusText = files.Count > 0
            ? $"{files.Count} file(s) ready to harvest."
            : "Resolve some files first.";
    }

    private bool IsAutoDefaultName(string currentName)
    {
        // If the current name matches what we would auto-generate for the
        // previous file set, treat it as auto-managed.
        return _resolvedFiles.Count > 0
            && currentName == Path.GetFileNameWithoutExtension(
                _resolvedFiles[0].AbsolutePath);
    }

    private async Task RunHarvestAsync()
    {
        if (_isHarvesting) return;
        if (_selectedFormat is null) return;
        if (_resolvedFiles.Count == 0) return;

        IsHarvesting = true;
        StatusText = "Harvesting...";
        _cts = new CancellationTokenSource();

        try
        {
            var options = new HarvestOptions(
                DestinationFolder: string.IsNullOrWhiteSpace(_destinationPath)
                    ? null : _destinationPath,
                FileName: string.IsNullOrWhiteSpace(_outputFileName)
                    ? null : _outputFileName,
                Extension: _selectedFormat.Extension,
                Mode: _selectedMode,
                DelimiterTemplate: ComposedDocument.DefaultDelimiter,
                TwoBlankLines: _twoBlankLines,
                IncludeTableOfContents: _includeTableOfContents,
                IncludeManifest: _includeManifest,
                PreserveFolderStructure: _preserveFolderStructure,
                IncludeHeaders: _includeHeaders,
                IncludeCodeFences: _includeCodeFences);

            var progress = new Progress<HarvestProgress>(p =>
            {
                StatusText = $"{p.Stage} ({p.Completed}/{p.Total})";
            });

            var result = await _runner.RunAsync(
                _resolvedFiles, options, progress, _cts.Token);

            StatusText = result.Success
                ? $"Saved to {result.OutputPath}"
                : $"Failed: {result.ErrorMessage}";

            HarvestCompleted?.Invoke(this, result);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Harvest cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Harvest failed: {ex.Message}";
            HarvestCompleted?.Invoke(this, SaveResult.Fail(_destinationPath, ex.Message));
        }
        finally
        {
            IsHarvesting = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _cts?.Dispose();
        _cts = null;
        _disposed = true;
    }
}
