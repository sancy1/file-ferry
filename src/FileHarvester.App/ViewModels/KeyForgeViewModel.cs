// ============================================================================
// filepath: src/FileHarvester.App/ViewModels/KeyForgeViewModel.cs
// layer:    App / ViewModels
// module:   FileHarvester.App.ViewModels
// since:    Phase A (KeyForge)
// purpose:  Sub-ViewModel for the KeyForge tool (a sub-app of the Deployment
//           Vault). Owns the original/forged text panes, the key extraction,
//           the shape-preserving forgery, the detected-keys panel, and the
//           save/copy flow.
//
// RELATIONSHIPS
//   Implements : FileHarvester.App.ViewModels.ViewModelBase
//   Depends on : DeploymentVault.Core.KeyForge.Abstractions.* (IKeyExtractor,
//                IKeyForger, IDocumentTextReader),
//                DeploymentVault.Core.KeyForge.Domain.* (KeyFamily, FakePolicy,
//                KeySpan, DetectedKey)
//   Used by    : DeploymentVaultViewModel.KeyForge; KeyForgePage.axaml
//   See also   : DeploymentVaultViewModel (the parent that owns this VM),
//                EnvSafeViewModel (the sibling sub-app VM)
//
// NOTES
//   - Avalonia-free. File pickers, the confirm delegate, the save-as picker,
//     and the clipboard are injected delegates so this VM is unit-testable
//     without an Avalonia harness.
//   - Convert extracts key-shaped spans and forges a shape-identical fake for
//     each, preserving surrounding text character-for-character.
//   - FakePolicy defaults to Deterministic (same input -> same fake). The
//     Regenerate command re-forges with Random policy.
//   - Save default: Desktop + keyforge-{yyyyMMdd-HHmmss}.txt. The output is
//     always text (KeyForge does not write back into the original container).
// ============================================================================

using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using DeploymentVault.Core.KeyForge.Abstractions;
using DeploymentVault.Core.KeyForge.Domain;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The KeyForge tool view model.
/// </summary>
public sealed class KeyForgeViewModel : ViewModelBase
{
    private readonly IKeyExtractor _extractor;
    private readonly IKeyForger _forger;
    private readonly IDocumentTextReader _reader;
    private readonly Func<string, string, Task<bool>>? _confirm;
    private readonly Func<string, string, string, Task<string?>>? _saveFile;
    private readonly Func<Task<string?>>? _pickDocument;
    private readonly Func<string, Task>? _copyToClipboard;
    private readonly string? _desktopDirectory;

    private string _originalText = string.Empty;
    private string _forgedText = string.Empty;
    private string _sourceLabel = "Pasted";
    private string _statusText = "Paste or import content to begin.";
    private string _summaryText = string.Empty;
    private string _saveFileName = string.Empty;
    private FakePolicy _fakePolicy = FakePolicy.Deterministic;
    private bool _isBusy;
    private bool _hasOutput;
    private bool _canConvert;
    private List<KeySpan> _lastSpans = new();

    /// <summary>Builds the KeyForge view model.</summary>
    /// <param name="extractor">The key extractor. Not null.</param>
    /// <param name="forger">The shape-preserving forger. Not null.</param>
    /// <param name="reader">The document text reader. Not null.</param>
    /// <param name="confirm">Overwrite confirm delegate (title, message) -> bool. Optional.</param>
    /// <param name="saveFile">Save-as picker delegate (title, suggestedName, extension) -> path or null. Optional.</param>
    /// <param name="pickDocument">Import file picker delegate -> path or null. Optional.</param>
    /// <param name="copyToClipboard">Clipboard delegate. Optional.</param>
    /// <param name="desktopDirectory">
    /// Override for the Desktop directory used by Save. Optional; when null the
    /// real Desktop is used. Tests pass a temp directory.
    /// </param>
    public KeyForgeViewModel(
        IKeyExtractor extractor,
        IKeyForger forger,
        IDocumentTextReader reader,
        Func<string, string, Task<bool>>? confirm = null,
        Func<string, string, string, Task<string?>>? saveFile = null,
        Func<Task<string?>>? pickDocument = null,
        Func<string, Task>? copyToClipboard = null,
        string? desktopDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(forger);
        ArgumentNullException.ThrowIfNull(reader);
        _extractor = extractor;
        _forger = forger;
        _reader = reader;
        _confirm = confirm;
        _saveFile = saveFile;
        _pickDocument = pickDocument;
        _copyToClipboard = copyToClipboard;
        _desktopDirectory = desktopDirectory;

        DetectedKeys = new ObservableCollection<DetectedKeyRowViewModel>();
        FakePolicies = new[] { FakePolicy.Deterministic, FakePolicy.Random };

        ImportFileCommand = new AsyncRelayCommand<object>(_ => ImportFileAsync());
        ConvertCommand = new AsyncRelayCommand<object>(_ => ConvertAsync());
        RegenerateCommand = new AsyncRelayCommand<object>(_ => RegenerateAsync());
        SaveForgedCommand = new AsyncRelayCommand<object>(_ => SaveForgedAsync());
        CopyForgedCommand = new AsyncRelayCommand<object>(_ => CopyForgedAsync());
        ClearCommand = new RelayCommand(Clear);
        ResetCommand = new RelayCommand(Reset);
    }

    /// <summary>The original content (editable).</summary>
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

    /// <summary>The forged content (editable).</summary>
    public string ForgedText
    {
        get => _forgedText;
        set
        {
            if (SetProperty(ref _forgedText, value))
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

    /// <summary>The conversion summary line.</summary>
    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    /// <summary>The desired output file name (default keyforge-{timestamp}.txt).</summary>
    public string SaveFileName
    {
        get => _saveFileName;
        set => SetProperty(ref _saveFileName, value);
    }

    /// <summary>The fake generation policy.</summary>
    public FakePolicy FakePolicy
    {
        get => _fakePolicy;
        set => SetProperty(ref _fakePolicy, value);
    }

    /// <summary>True while an async operation is running.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>True when the original pane has content.</summary>
    public bool HasInput => !string.IsNullOrEmpty(_originalText);

    /// <summary>True when the forged pane has content.</summary>
    public bool HasOutput
    {
        get => _hasOutput;
        private set => SetProperty(ref _hasOutput, value);
    }

    /// <summary>True when Convert is enabled (input has content).</summary>
    public bool CanConvert
    {
        get => _canConvert;
        private set => SetProperty(ref _canConvert, value);
    }

    /// <summary>The detected keys panel rows.</summary>
    public ObservableCollection<DetectedKeyRowViewModel> DetectedKeys { get; }

    /// <summary>The available fake policies for the ComboBox.</summary>
    public IReadOnlyList<FakePolicy> FakePolicies { get; }

    public ICommand ImportFileCommand { get; }
    public ICommand ConvertCommand { get; }
    public ICommand RegenerateCommand { get; }
    public ICommand SaveForgedCommand { get; }
    public ICommand CopyForgedCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand ResetCommand { get; }

    private void RefreshValidation()
    {
        CanConvert = HasInput;
    }

    private async Task ImportFileAsync()
    {
        if (_pickDocument is null)
        {
            StatusText = "File import is not available in this build.";
            return;
        }
        IsBusy = true;
        try
        {
            var path = await _pickDocument().ConfigureAwait(true);
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
            StatusText = "Paste or import content first.";
            return;
        }

        IsBusy = true;
        try
        {
            _lastSpans = _extractor.Extract(_originalText).ToList();
            if (_lastSpans.Count == 0)
            {
                ForgedText = _originalText;
                DetectedKeys.Clear();
                SummaryText = "No key-shaped strings detected. Output is unchanged.";
                StatusText = "No keys detected.";
                return;
            }

            var (forged, detected) = ForgeAll(_lastSpans, _fakePolicy);
            ForgedText = forged;
            PopulateDetectedKeys(detected);
            SummaryText = BuildSummary(detected);
            StatusText = $"Forged {detected.Count} key(s).";
        }
        catch (Exception ex)
        {
            StatusText = "Conversion failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
        await Task.CompletedTask.ConfigureAwait(true);
    }

    private async Task RegenerateAsync()
    {
        if (_lastSpans.Count == 0)
        {
            StatusText = "Nothing to regenerate yet. Convert first.";
            return;
        }

        IsBusy = true;
        try
        {
            var (forged, detected) = ForgeAll(_lastSpans, FakePolicy.Random);
            ForgedText = forged;
            PopulateDetectedKeys(detected);
            SummaryText = BuildSummary(detected);
            StatusText = $"Regenerated {detected.Count} fake(s) (random).";
        }
        catch (Exception ex)
        {
            StatusText = "Regenerate failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
        await Task.CompletedTask.ConfigureAwait(true);
    }

    private (string Forged, List<DetectedKey> Detected) ForgeAll(List<KeySpan> spans, FakePolicy policy)
    {
        var detected = new List<DetectedKey>(spans.Count);
        foreach (var span in spans)
        {
            var fake = _forger.Forge(span.Original, span.Family, policy);
            detected.Add(new DetectedKey(span.Start, span.Length, span.Family, span.Original, fake));
        }

        // Replace spans from end to start so earlier indices stay valid.
        var sb = new System.Text.StringBuilder(_originalText);
        for (var i = detected.Count - 1; i >= 0; i--)
        {
            var d = detected[i];
            sb.Remove(d.Start, d.Length);
            sb.Insert(d.Start, d.Fake);
        }
        return (sb.ToString(), detected);
    }

    private void PopulateDetectedKeys(List<DetectedKey> detected)
    {
        DetectedKeys.Clear();
        foreach (var d in detected)
        {
            DetectedKeys.Add(new DetectedKeyRowViewModel(d, _copyToClipboard));
        }
    }

    private static string BuildSummary(List<DetectedKey> detected)
    {
        if (detected.Count == 0) return string.Empty;
        var byFamily = detected
            .GroupBy(d => d.Family)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Count()} {g.Key}");
        return $"{detected.Count} key(s) detected: {string.Join(", ", byFamily)}.";
    }

    private async Task SaveForgedAsync()
    {
        if (!HasOutput)
        {
            StatusText = "Nothing to save yet.";
            return;
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
                    var picked = await _saveFile("Save KeyForge output", fileName, ".txt").ConfigureAwait(true);
                    if (string.IsNullOrWhiteSpace(picked))
                    {
                        StatusText = "Save cancelled.";
                        return;
                    }
                    path = picked;
                }
            }

            await File.WriteAllTextAsync(path, _forgedText).ConfigureAwait(true);
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

    private async Task CopyForgedAsync()
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
        await _copyToClipboard(_forgedText).ConfigureAwait(true);
        StatusText = "Forged output copied to clipboard.";
    }

    private void Clear()
    {
        OriginalText = string.Empty;
        ForgedText = string.Empty;
        SourceLabel = "Pasted";
        SummaryText = string.Empty;
        DetectedKeys.Clear();
        _lastSpans = new List<KeySpan>();
        StatusText = "Cleared.";
    }

    private void Reset()
    {
        Clear();
        SaveFileName = string.Empty;
        FakePolicy = FakePolicy.Deterministic;
        StatusText = "Reset.";
    }

    private string BuildFileName()
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var baseName = string.IsNullOrWhiteSpace(_saveFileName)
            ? $"keyforge-{stamp}.txt"
            : _saveFileName.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(baseName.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        if (sanitized.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return sanitized;
        return sanitized + ".txt";
    }
}
