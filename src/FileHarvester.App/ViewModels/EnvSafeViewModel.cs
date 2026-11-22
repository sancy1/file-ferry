// ============================================================================
// filepath: src/FileHarvester.App/ViewModels/EnvSafeViewModel.cs
// layer:    App / ViewModels
// module:   FileHarvester.App.ViewModels
// since:    Phase A (EnvSafe)
// purpose:  Sub-ViewModel for the EnvSafe tool (a sub-app of the Deployment
//           Vault). Owns the original/converted text panes, the validation
//           gate, the conversion, the safety scan, and the save flow.
//
// RELATIONSHIPS
//   Implements : FileHarvester.App.ViewModels.ViewModelBase
//   Depends on : DeploymentVault.Core.EnvSafe.Abstractions.* (IEnvFileValidator,
//                IEnvFileConverter, ISafetyScanner, IEnvFileReader),
//                DeploymentVault.Core.EnvSafe.Domain.* (PlaceholderStyle,
//                SafetyWarning)
//   Used by    : DeploymentVaultViewModel.EnvSafe; EnvSafePage.axaml
//   See also   : DeploymentVaultViewModel (the parent that owns this VM)
//
// NOTES
//   - Avalonia-free. File pickers, the overwrite confirm, the safety
//     confirm, the save-as picker, and the clipboard are injected delegates
//     so this VM is unit-testable without an Avalonia harness.
//   - The validation gate: Convert is disabled when the input has no
//     KEY=value pairs. The gate re-evaluates on every OriginalText change.
//   - The safety scan runs automatically after every conversion and can be
//     re-run manually. Download requires acknowledgement when warnings exist.
//   - Save default: Desktop + .env.example. If the file exists, the user is
//     asked to replace or save-as. The output format (.env.example/.txt/.md)
//     is never .env — that would undo the safety purpose.
// ============================================================================

using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using DeploymentVault.Core.EnvSafe.Abstractions;
using DeploymentVault.Core.EnvSafe.Domain;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The EnvSafe tool view model.
/// </summary>
public sealed class EnvSafeViewModel : ViewModelBase
{
    private readonly IEnvFileValidator _validator;
    private readonly IEnvFileConverter _converter;
    private readonly ISafetyScanner _scanner;
    private readonly IEnvFileReader _reader;
    private readonly Func<string, string, Task<bool>>? _confirm;
    private readonly Func<string, string, string, Task<string?>>? _saveFile;
    private readonly Func<Task<string?>>? _pickEnvFile;
    private readonly Func<string, Task>? _copyToClipboard;
    private readonly string? _desktopDirectory;

    private string _originalText = string.Empty;
    private string _convertedText = string.Empty;
    private string _sourceLabel = "Pasted";
    private string _statusText = "Paste or import .env content to begin.";
    private string _validationMessage = string.Empty;
    private string _summaryText = string.Empty;
    private string _saveFileName = ".env.example";
    private string _outputFormat = ".env.example";
    private PlaceholderStyle _placeholderStyle = PlaceholderStyle.YourXHere;
    private bool _isBusy;
    private bool _hasOutput;
    private bool _canConvert;
    private bool _hasWarnings;

    /// <summary>Builds the EnvSafe view model.</summary>
    /// <param name="validator">The KEY=value validator. Not null.</param>
    /// <param name="converter">The .env -> .env.example converter. Not null.</param>
    /// <param name="scanner">The safety scanner. Not null.</param>
    /// <param name="reader">The source file reader. Not null.</param>
    /// <param name="confirm">Overwrite/safety confirm delegate (title, message) -> bool. Optional.</param>
    /// <param name="saveFile">Save-as picker delegate (title, suggestedName, extension) -> path or null. Optional.</param>
    /// <param name="pickEnvFile">Import file picker delegate -> path or null. Optional.</param>
    /// <param name="copyToClipboard">Clipboard delegate. Optional.</param>
    /// <param name="desktopDirectory">
    /// Override for the Desktop directory used by Download. Optional; when
    /// null the real Desktop is used. Tests pass a temp directory.
    /// </param>
    public EnvSafeViewModel(
        IEnvFileValidator validator,
        IEnvFileConverter converter,
        ISafetyScanner scanner,
        IEnvFileReader reader,
        Func<string, string, Task<bool>>? confirm = null,
        Func<string, string, string, Task<string?>>? saveFile = null,
        Func<Task<string?>>? pickEnvFile = null,
        Func<string, Task>? copyToClipboard = null,
        string? desktopDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(converter);
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(reader);
        _validator = validator;
        _converter = converter;
        _scanner = scanner;
        _reader = reader;
        _confirm = confirm;
        _saveFile = saveFile;
        _pickEnvFile = pickEnvFile;
        _copyToClipboard = copyToClipboard;
        _desktopDirectory = desktopDirectory;

        Warnings = new ObservableCollection<SafetyWarning>();
        PlaceholderStyles = new[]
        {
            PlaceholderStyle.YourXHere,
            PlaceholderStyle.UpperXHere,
            PlaceholderStyle.AngleXHere,
            PlaceholderStyle.ChangeMe,
        };
        OutputFormats = new[] { ".env.example", ".txt", ".md" };

        ImportFileCommand = new AsyncRelayCommand<object>(_ => ImportFileAsync());
        ConvertCommand = new AsyncRelayCommand<object>(_ => ConvertAsync());
        SafetyScanCommand = new AsyncRelayCommand<object>(_ => SafetyScanAsync());
        DownloadCommand = new AsyncRelayCommand<object>(_ => DownloadAsync());
        CopyOutputCommand = new AsyncRelayCommand<object>(_ => CopyOutputAsync());
        ClearCommand = new RelayCommand(Clear);
        ResetCommand = new RelayCommand(Reset);
    }

    /// <summary>The original .env content (editable).</summary>
    public string OriginalText
    {
        get => _originalText;
        set
        {
            if (SetProperty(ref _originalText, value))
            {
                RefreshValidation();
                OnPropertyChanged(nameof(HasInput));
            }
        }
    }

    /// <summary>The converted .env.example content (editable).</summary>
    public string ConvertedText
    {
        get => _convertedText;
        set
        {
            if (SetProperty(ref _convertedText, value))
            {
                HasOutput = !string.IsNullOrEmpty(value);
            }
        }
    }

    /// <summary>Where the original content came from.</summary>
    public string SourceLabel
    {
        get => _sourceLabel;
        private set => SetProperty(ref _sourceLabel, value);
    }

    /// <summary>The status line.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>The validation gate message (empty when valid).</summary>
    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    /// <summary>The conversion summary line.</summary>
    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    /// <summary>The desired output file name (default .env.example).</summary>
    public string SaveFileName
    {
        get => _saveFileName;
        set => SetProperty(ref _saveFileName, value);
    }

    /// <summary>The output format extension (.env.example, .txt, .md).</summary>
    public string OutputFormat
    {
        get => _outputFormat;
        set => SetProperty(ref _outputFormat, value);
    }

    /// <summary>The placeholder style.</summary>
    public PlaceholderStyle PlaceholderStyle
    {
        get => _placeholderStyle;
        set
        {
            if (SetProperty(ref _placeholderStyle, value))
            {
                RefreshValidation();
            }
        }
    }

    /// <summary>True while an async operation is running.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>True when the original pane has content.</summary>
    public bool HasInput => !string.IsNullOrEmpty(_originalText);

    /// <summary>True when the converted pane has content.</summary>
    public bool HasOutput
    {
        get => _hasOutput;
        private set => SetProperty(ref _hasOutput, value);
    }

    /// <summary>True when Convert is enabled (input has KEY=value pairs).</summary>
    public bool CanConvert
    {
        get => _canConvert;
        private set => SetProperty(ref _canConvert, value);
    }

    /// <summary>True when the safety scan found warnings.</summary>
    public bool HasWarnings
    {
        get => _hasWarnings;
        private set => SetProperty(ref _hasWarnings, value);
    }

    /// <summary>The safety warnings from the last scan.</summary>
    public ObservableCollection<SafetyWarning> Warnings { get; }

    /// <summary>The available placeholder styles for the ComboBox.</summary>
    public IReadOnlyList<PlaceholderStyle> PlaceholderStyles { get; }

    /// <summary>The available output formats for the ComboBox.</summary>
    public IReadOnlyList<string> OutputFormats { get; }

    public ICommand ImportFileCommand { get; }
    public ICommand ConvertCommand { get; }
    public ICommand SafetyScanCommand { get; }
    public ICommand DownloadCommand { get; }
    public ICommand CopyOutputCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand ResetCommand { get; }

    private void RefreshValidation()
    {
        var hasPairs = HasInput && _validator.HasKeyValuePairs(_originalText);
        CanConvert = hasPairs;
        ValidationMessage = hasPairs || !HasInput
            ? string.Empty
            : "No valid environment variables were detected.\nExpected format: KEY=value\nExample: API_KEY=your_api_key";
    }

    private async Task ImportFileAsync()
    {
        if (_pickEnvFile is null)
        {
            StatusText = "File import is not available in this build.";
            return;
        }
        IsBusy = true;
        try
        {
            var path = await _pickEnvFile().ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(path))
            {
                StatusText = "Import cancelled.";
                return;
            }
            var content = await _reader.ReadAsync(path).ConfigureAwait(true);
            OriginalText = content;
            SourceLabel = "Uploaded: " + Path.GetFileName(path);
            StatusText = $"Imported {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            StatusText = "Import failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConvertAsync()
    {
        if (!HasInput)
        {
            StatusText = "Paste or import .env content first.";
            return;
        }
        if (!_validator.HasKeyValuePairs(_originalText))
        {
            RefreshValidation();
            StatusText = "No valid environment variables were detected.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = _converter.Convert(_originalText, _placeholderStyle);
            if (!result.Success)
            {
                ValidationMessage = result.Error ?? "Conversion failed.";
                StatusText = "Conversion failed.";
                return;
            }

            ConvertedText = result.Output ?? string.Empty;
            await RunSafetyScanAsync().ConfigureAwait(true);

            var warningCount = Warnings.Count;
            SummaryText = $"{result.VariablesDetected} variables detected, "
                + $"{result.CommentsPreserved} comments preserved, "
                + $"{result.ValuesReplaced} values replaced, "
                + $"{warningCount} warning(s).";
            StatusText = warningCount > 0
                ? $"Conversion complete. {warningCount} safety warning(s) — review before saving."
                : "Conversion complete.";
        }
        catch (Exception ex)
        {
            StatusText = "Conversion failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SafetyScanAsync()
    {
        if (!HasOutput)
        {
            StatusText = "Nothing to scan yet.";
            return;
        }
        IsBusy = true;
        try
        {
            await RunSafetyScanAsync().ConfigureAwait(true);
            StatusText = Warnings.Count == 0
                ? "Safety scan clean."
                : $"Safety scan found {Warnings.Count} warning(s).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunSafetyScanAsync()
    {
        Warnings.Clear();
        var warnings = _scanner.Scan(_convertedText, _placeholderStyle);
        foreach (var w in warnings) Warnings.Add(w);
        HasWarnings = Warnings.Count > 0;
        await Task.CompletedTask.ConfigureAwait(true);
    }

    private async Task DownloadAsync()
    {
        if (!HasOutput)
        {
            StatusText = "Nothing to save yet.";
            return;
        }

        if (HasWarnings && _confirm is not null)
        {
            var ok = await _confirm(
                "Safety warnings",
                $"{Warnings.Count} potential secret(s) remain in the output.\n\nSave anyway?").ConfigureAwait(true);
            if (!ok)
            {
                StatusText = "Save cancelled.";
                return;
            }
        }

        IsBusy = true;
        try
        {
            var fileName = BuildFileName();
            var desktop = _desktopDirectory
                ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var path = Path.Combine(desktop, fileName);

            if (File.Exists(path) && _confirm is not null)
            {
                var replace = await _confirm(
                    "File exists",
                    $"'{fileName}' already exists on the Desktop.\nReplace it?").ConfigureAwait(true);
                if (!replace)
                {
                    if (_saveFile is null)
                    {
                        StatusText = "Save cancelled.";
                        return;
                    }
                    var picked = await _saveFile("Save EnvSafe output", fileName, OutputFormat).ConfigureAwait(true);
                    if (string.IsNullOrWhiteSpace(picked))
                    {
                        StatusText = "Save cancelled.";
                        return;
                    }
                    path = picked;
                }
            }

            await File.WriteAllTextAsync(path, _convertedText).ConfigureAwait(true);
            StatusText = $"Saved to {path}.";
        }
        catch (Exception ex)
        {
            StatusText = "Save failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CopyOutputAsync()
    {
        if (!HasOutput)
        {
            StatusText = "Nothing to copy yet.";
            return;
        }
        if (_copyToClipboard is null)
        {
            StatusText = "Copy is not available in this build.";
            return;
        }
        await _copyToClipboard(_convertedText).ConfigureAwait(true);
        StatusText = "Output copied to clipboard.";
    }

    private void Clear()
    {
        OriginalText = string.Empty;
        ConvertedText = string.Empty;
        SourceLabel = "Pasted";
        SummaryText = string.Empty;
        Warnings.Clear();
        HasWarnings = false;
        ValidationMessage = string.Empty;
        StatusText = "Cleared.";
    }

    private void Reset()
    {
        Clear();
        SaveFileName = ".env.example";
        OutputFormat = ".env.example";
        PlaceholderStyle = PlaceholderStyle.YourXHere;
        StatusText = "Reset.";
    }

    private string BuildFileName()
    {
        var baseName = string.IsNullOrWhiteSpace(_saveFileName) ? ".env.example" : _saveFileName.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(baseName.Select(c => invalid.Contains(c) ? '_' : c).ToArray());

        if (_outputFormat == ".env.example") return sanitized;
        if (sanitized.EndsWith(_outputFormat, StringComparison.OrdinalIgnoreCase)) return sanitized;
        return sanitized + _outputFormat;
    }
}
