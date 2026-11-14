// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/PromptExportDialogViewModel.cs
// PURPOSE:  The export dialog's view model. Holds the format dropdown (from
//           the IFormatWriterRegistry), the destination folder, the output
//           file name, the overwrite/open-folder flags, and the Export /
//           Cancel commands. Calls IPromptExporter.ExportSingleAsync.
// RELATION: Constructed by PromptPage.axaml.cs when the viewer's Export
//           button is clicked. Bound to PromptExportDialog.axaml. Uses
//           IPromptExporter (P3.2), IFormatWriterRegistry (FileHarvester.
//           Core.Writers), and FormatOptionViewModel (the harvester's
//           existing format-option type).
// NOTES:    - The destination defaults to the user's Documents folder
//             (Environment.SpecialFolder.MyDocuments). The user can browse
//             to any folder via the dialog's Browse button (the code-behind
//             handles the native folder picker).
//           - The output file name defaults to the sanitized prompt title;
//             the user can override it.
//           - The overwrite decision is delegated to the PromptExporter's
//             IOverwriteConfirmer (the harvester's modal). The OverwriteExisting
//             checkbox is informational in P3.4; the confirmer is the real
//             prompt. A future refinement can skip the confirmer when the
//             checkbox is set.
//           - The "Open folder after save" launches the OS's file manager
//             at the exported file's folder.
//           - The VM is dialog-agnostic: it raises Saved/Cancelled; the
//             code-behind closes the dialog on either.
// ============================================================================

using System.Collections.ObjectModel;
using System.Diagnostics;
using FileHarvester.Core.Writers;
using Prompt.Core.Domain;
using PromptRecord = Prompt.Core.Domain.Prompt;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The export dialog's view model.
/// </summary>
public sealed class PromptExportDialogViewModel : ViewModelBase
{
    private readonly IPromptExporter _exporter;
    private readonly PromptRecord? _prompt;
    private readonly IReadOnlyList<PromptRecord>? _prompts;

    private FormatOptionViewModel? _selectedFormat;
    private string _destinationPath = string.Empty;
    private string _outputFileName = string.Empty;
    private bool _overwriteExisting;
    private bool _openFolderAfterSave = true;
    private string? _statusText;
    private bool _isExporting;
    private bool _saveToZip;

    /// <summary>Creates the dialog VM.</summary>
    public PromptExportDialogViewModel(
        IPromptExporter exporter,
        IFormatWriterRegistry registry,
        PromptRecord prompt)
    {
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(prompt);
        _prompt = prompt;

        Formats = new ObservableCollection<FormatOptionViewModel>(
            registry.AllWriters.Where(w => w.Extension != ".zip").Select(w =>
                new FormatOptionViewModel(w.Extension, w.DisplayName, w.MimeType)));

        if (Formats.Count > 0) { _selectedFormat = Formats[0]; }

        _destinationPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _outputFileName = SanitizeFileName(prompt.Title);
        _saveToZip = false;

        ExportCommand = new AsyncRelayCommand(OnExportAsync);
        CancelCommand = new RelayCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));
        SelectDestinationCommand = new RelayCommand(() => { /* code-behind */ });
    }

    /// <summary>Creates the dialog VM for the many (the bulk export).</summary>
    public PromptExportDialogViewModel(
        IPromptExporter exporter,
        IFormatWriterRegistry registry,
        IReadOnlyList<PromptRecord> prompts)
    {
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(prompts);
        _prompt = null;
        _prompts = prompts;

        Formats = new ObservableCollection<FormatOptionViewModel>(
            registry.AllWriters.Where(w => w.Extension != ".zip").Select(w =>
                new FormatOptionViewModel(w.Extension, w.DisplayName, w.MimeType)));
        if (Formats.Count > 0) { _selectedFormat = Formats[0]; }

        _destinationPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _outputFileName = "PromptExport";
        _saveToZip = true;

        ExportCommand = new AsyncRelayCommand(OnExportAsync);
        CancelCommand = new RelayCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));
        SelectDestinationCommand = new RelayCommand(() => { /* code-behind */ });
    }

    /// <summary>The format options, from the registry's writers.</summary>
    public ObservableCollection<FormatOptionViewModel> Formats { get; }

    /// <summary>The selected format.</summary>
    public FormatOptionViewModel? SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (SetProperty(ref _selectedFormat, value))
            {
                OnPropertyChanged(nameof(CanExport));
                OnPropertyChanged(nameof(FilePreviewText));
            }
        }
    }

    /// <summary>The destination folder.</summary>
    public string DestinationPath
    {
        get => _destinationPath;
        set
        {
            if (SetProperty(ref _destinationPath, value))
            {
                OnPropertyChanged(nameof(CanExport));
            }
        }
    }

    /// <summary>The output file name (without extension).</summary>
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

    /// <summary>Informational in P3.4; the confirmer is the real overwrite prompt.</summary>
    public bool OverwriteExisting
    {
        get => _overwriteExisting;
        set => SetProperty(ref _overwriteExisting, value);
    }

    /// <summary>True = open the OS file manager after a successful save.</summary>
    public bool OpenFolderAfterSave
    {
        get => _openFolderAfterSave;
        set => SetProperty(ref _openFolderAfterSave, value);
    }

    /// <summary>A short status message.</summary>
    public string? StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>True while the export is running.</summary>
    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            if (SetProperty(ref _isExporting, value))
            {
                OnPropertyChanged(nameof(CanExport));
            }
        }
    }

    /// <summary>True when the Save to zip checkbox is checked (the single's the zip's the container).</summary>
    public bool SaveToZip
    {
        get => _saveToZip;
        set
        {
            if (SetProperty(ref _saveToZip, value))
            {
                OnPropertyChanged(nameof(FilePreviewText));
            }
        }
    }

    /// <summary>True when the dialog is the single's (the checkbox is enabled); false for the many.</summary>
    public bool IsSingleExport => _prompts is null;

    /// <summary>True when the Export button is enabled.</summary>
    public bool CanExport =>
        !_isExporting
        && _selectedFormat is not null
        && !string.IsNullOrWhiteSpace(_destinationPath)
        && (_prompt is not null || (_prompts is { Count: > 0 }));

    /// <summary>The file name with extension for the preview.</summary>
    public string FilePreviewText
    {
        get
        {
            if (_prompts is not null) return "PromptExport.zip";
            if (_saveToZip) return "PromptExport.zip";
            var ext = _selectedFormat?.Extension ?? ".?";
            var baseName = string.IsNullOrWhiteSpace(_outputFileName) ? "export" : _outputFileName.Trim();
            return baseName.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? baseName : baseName + ext;
        }
    }

    /// <summary>Exports the prompt.</summary>
    public AsyncRelayCommand ExportCommand { get; }

    /// <summary>Closes the dialog without saving.</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>The code-behind's folder picker hook.</summary>
    public RelayCommand SelectDestinationCommand { get; }

    /// <summary>Raised on a successful export; the dialog closes with the result.</summary>
    public event EventHandler<ExportResult>? Saved;

    /// <summary>Raised on Cancel; the dialog closes with null.</summary>
    public event EventHandler? Cancelled;

    private async Task OnExportAsync()
    {
        if (!CanExport) return;
        IsExporting = true;
        StatusText = "Exporting...";
        try
        {
            var result = _prompts is not null
                ? await _exporter.ExportManyAsync(_prompts, _selectedFormat!.Extension, _destinationPath, _outputFileName).ConfigureAwait(true)
                : _saveToZip
                    ? await _exporter.ExportManyAsync(new[] { _prompt! }, _selectedFormat!.Extension, _destinationPath, _outputFileName).ConfigureAwait(true)
                    : await _exporter.ExportSingleAsync(_prompt!, _selectedFormat!.Extension, _destinationPath, _outputFileName).ConfigureAwait(true);

            if (result.Success)
            {
                StatusText = "Exported to " + (result.OutputPath ?? "(unknown)");
                if (_openFolderAfterSave && result.OutputPath is not null)
                {
                    TryOpenFolder(result.OutputPath);
                }
                Saved?.Invoke(this, result);
            }
            else
            {
                StatusText = "Export failed: " + (result.ErrorMessage ?? "unknown");
            }
        }
        catch (Exception ex)
        {
            StatusText = "Export failed: " + ex.Message;
        }
        finally
        {
            IsExporting = false;
        }
    }

    private static void TryOpenFolder(string path)
    {
        try
        {
            var folder = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(folder)) return;
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch
        {
            // Best-effort; a failure to open the folder must not fail the export.
        }
    }

    private static string SanitizeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(title.Length);
        foreach (var c in title) { sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c); }
        var result = sb.ToString().Trim();
        return result.Length == 0 ? "export" : result;
    }
}