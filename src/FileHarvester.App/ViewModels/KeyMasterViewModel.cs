// ============================================================================
// filepath: src/FileHarvester.App/ViewModels/KeyMasterViewModel.cs
// layer:    App / ViewModels
// module:   FileHarvester.App.ViewModels
// purpose:  Sub-ViewModel for the KeyMaster tool (a sub-app of the Deployment
//           Vault). Owns framework/kind selection, per-kind generation
//           controls, the entropy meter, masked output with reveal/copy,
//           batch generation, Pro-mode sample matching, and the session-only
//           history panel.
//
// RELATIONSHIPS
//   Implements : FileHarvester.App.ViewModels.ViewModelBase
//   Depends on : DeploymentVault.Core.KeyMaster.Abstractions.* (registry,
//                store), DeploymentVault.Core.KeyMaster.Domain.* (profiles,
//                spec, entropy, sample analyzer),
//                DeploymentVault.Platform.KeyMaster.Generation.* (charset
//                helpers for the live entropy meter)
//   Used by    : DeploymentVaultViewModel.KeyMaster; KeyMasterPage.axaml
//   See also   : KeyForgeViewModel, EnvSafeViewModel (sibling sub-app VMs)
//
// NOTES
//   - Avalonia-free. The clipboard is an injected delegate so the VM is
//     unit-testable without an Avalonia harness.
//   - No persistence: history lives in IKeyMasterStore (in-memory, last 5).
//   - Generation always goes through IKeyGeneratorRegistry, which applies
//     the keymaster-generate resilience policy.
// ============================================================================

using System.Collections.ObjectModel;
using System.Windows.Input;
using DeploymentVault.Core.KeyMaster.Abstractions;
using DeploymentVault.Core.KeyMaster.Domain;
using DeploymentVault.Platform.KeyMaster.Generation;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The KeyMaster view model.
/// </summary>
public sealed class KeyMasterViewModel : ViewModelBase
{
    private readonly IKeyGeneratorRegistry _registry;
    private readonly IKeyMasterStore _store;
    private readonly Func<string, Task>? _copyToClipboard;

    private KeyCategory _selectedCategory = KeyCategory.NodeJs;
    private FrameworkProfile? _selectedProfile;
    private string _algorithm = string.Empty;
    private string _curve = "P-256";
    private int _modulusBits = 2048;
    private int _length = 32;
    private KeyFormat _format = KeyFormat.Base64Url;
    private int _batchCount = 1;
    private bool _useUpper = true;
    private bool _useLower = true;
    private bool _useDigits = true;
    private bool _useSymbols = true;
    private string _exclusions = string.Empty;
    private string _prefix = string.Empty;
    private string _suffix = string.Empty;
    private string _sampleText = string.Empty;
    private string _sampleProfileText = string.Empty;
    private GeneratedKey? _currentKey;
    private bool _isRevealed;
    private bool _isBusy;
    private string _statusText = "Select a framework and generate.";
    private string _errorText = string.Empty;

    /// <summary>Builds the KeyMaster view model.</summary>
    /// <param name="registry">The generator registry. Not null.</param>
    /// <param name="store">The session-only store. Not null.</param>
    /// <param name="copyToClipboard">Clipboard delegate. Optional.</param>
    public KeyMasterViewModel(
        IKeyGeneratorRegistry registry,
        IKeyMasterStore store,
        Func<string, Task>? copyToClipboard = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(store);
        _registry = registry;
        _store = store;
        _copyToClipboard = copyToClipboard;

        Categories = FrameworkProfile.CategoryOrder;
        Profiles = new ObservableCollection<FrameworkProfile>(
            FrameworkProfile.All.Where(p => p.Category == _selectedCategory));
        History = new ObservableCollection<KeyMasterHistoryRowViewModel>();
        BatchResults = new ObservableCollection<GeneratedKey>();
        ModulusOptions = new[] { 2048, 3072, 4096 };
        CurveOptions = new[] { "P-256", "P-384", "P-521" };
        BatchOptions = new[] { 1, 5, 10, 25, 50, 100 };

        // Default selection: first profile of the first category.
        SelectedProfile = Profiles.Count > 0 ? Profiles[0] : null;

        GenerateCommand = new AsyncRelayCommand<object>(_ => GenerateAsync(1));
        GenerateBatchCommand = new AsyncRelayCommand<object>(_ => GenerateAsync(_batchCount));
        RegenerateCommand = new AsyncRelayCommand<object>(_ => GenerateAsync(1));
        CopyCommand = new AsyncRelayCommand<object>(_ => CopyAsync(false));
        CopyPublicCommand = new AsyncRelayCommand<object>(_ => CopyAsync(true));
        RevealCommand = new RelayCommand(ToggleReveal);
        AnalyzeSampleCommand = new RelayCommand(AnalyzeSample);
        MatchSampleCommand = new RelayCommand(MatchSample);
        ClearCommand = new RelayCommand(Clear);
        SelectProfileCommand = new RelayCommand<FrameworkProfile>(p =>
        {
            if (p is not null) SelectedProfile = p;
        });
    }

    // ---------------------------------------------------------------
    // Selection
    // ---------------------------------------------------------------

    /// <summary>The nav categories.</summary>
    public IReadOnlyList<KeyCategory> Categories { get; }

    /// <summary>Profiles in the selected category.</summary>
    public ObservableCollection<FrameworkProfile> Profiles { get; }

    /// <summary>The selected category.</summary>
    public KeyCategory SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                Profiles.Clear();
                foreach (var p in FrameworkProfile.All.Where(p => p.Category == value))
                {
                    Profiles.Add(p);
                }
                SelectedProfile = Profiles.Count > 0 ? Profiles[0] : null;
            }
        }
    }

    /// <summary>The selected framework profile (drives all controls).</summary>
    public FrameworkProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value) && value is not null)
            {
                ApplyProfileDefaults(value);
                RefreshVisibility();
                RefreshEntropy();
            }
        }
    }

    private void ApplyProfileDefaults(FrameworkProfile profile)
    {
        _length = profile.DefaultLength;
        _format = profile.PreferredFormat;
        OnPropertyChanged(nameof(Length));
        OnPropertyChanged(nameof(Format));

        AlgorithmOptions = AlgorithmOptionsFor(profile.Kind);
        _algorithm = AlgorithmOptions.Count > 0 ? AlgorithmOptions[0] : string.Empty;
        OnPropertyChanged(nameof(Algorithm));
        OnPropertyChanged(nameof(AlgorithmOptions));

        LengthOptions = profile.Kind == KeyKind.GoAesGcm ? new[] { 16, 24, 32 } : null;
        OnPropertyChanged(nameof(LengthOptions));

        FormatOptions = FormatOptionsFor(profile.Kind);
        OnPropertyChanged(nameof(FormatOptions));
        if (!FormatOptions.Contains(_format))
        {
            _format = profile.PreferredFormat;
            OnPropertyChanged(nameof(Format));
        }
        OnPropertyChanged(nameof(IsFormatSupported));

        _currentKey = null;
        IsRevealed = false;
        BatchResults.Clear();
        OnPropertyChanged(nameof(CurrentKey));
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(DisplayPublicValue));
        OnPropertyChanged(nameof(HasCurrentKey));
        OnPropertyChanged(nameof(IsKeyPair));
    }

    // ---------------------------------------------------------------
    // Generation controls
    // ---------------------------------------------------------------

    /// <summary>Algorithm variant options for the selected kind.</summary>
    public IReadOnlyList<string> AlgorithmOptions { get; private set; } = Array.Empty<string>();

    /// <summary>Selected algorithm variant.</summary>
    public string Algorithm
    {
        get => _algorithm;
        set
        {
            if (SetProperty(ref _algorithm, value))
            {
                RefreshLengthBounds();
                RefreshEntropy();
            }
        }
    }

    /// <summary>EC curve (ECDSA kinds).</summary>
    public string Curve
    {
        get => _curve;
        set => SetProperty(ref _curve, value);
    }

    /// <summary>RSA modulus bits.</summary>
    public int ModulusBits
    {
        get => _modulusBits;
        set => SetProperty(ref _modulusBits, value);
    }

    /// <summary>Length in bytes (binary kinds) or characters (Django/Pro).</summary>
    public int Length
    {
        get => _length;
        set
        {
            if (SetProperty(ref _length, value))
            {
                RefreshEntropy();
            }
        }
    }

    /// <summary>Preset length options (AES-GCM: 16/24/32); null = free slider.</summary>
    public IReadOnlyList<int>? LengthOptions { get; private set; }

    /// <summary>Output format.</summary>
    public KeyFormat Format
    {
        get => _format;
        set
        {
            if (SetProperty(ref _format, value))
            {
                OnPropertyChanged(nameof(IsFormatSupported));
            }
        }
    }

    /// <summary>Formats the selected kind actually supports.</summary>
    public IReadOnlyList<KeyFormat> FormatOptions { get; private set; } = Array.Empty<KeyFormat>();

    /// <summary>True when the selected format is supported by the selected kind.</summary>
    public bool IsFormatSupported => FormatOptions.Count == 0 || FormatOptions.Contains(_format);

    /// <summary>RSA modulus options.</summary>
    public IReadOnlyList<int> ModulusOptions { get; }

    /// <summary>EC curve options.</summary>
    public IReadOnlyList<string> CurveOptions { get; }

    /// <summary>Batch size options.</summary>
    public IReadOnlyList<int> BatchOptions { get; }

    /// <summary>Selected batch size.</summary>
    public int BatchCount
    {
        get => _batchCount;
        set => SetProperty(ref _batchCount, value);
    }

    // ---------------------------------------------------------------
    // Pro-mode controls
    // ---------------------------------------------------------------

    public bool UseUpper { get => _useUpper; set { if (SetProperty(ref _useUpper, value)) RefreshEntropy(); } }
    public bool UseLower { get => _useLower; set { if (SetProperty(ref _useLower, value)) RefreshEntropy(); } }
    public bool UseDigits { get => _useDigits; set { if (SetProperty(ref _useDigits, value)) RefreshEntropy(); } }
    public bool UseSymbols { get => _useSymbols; set { if (SetProperty(ref _useSymbols, value)) RefreshEntropy(); } }

    public string Exclusions
    {
        get => _exclusions;
        set { if (SetProperty(ref _exclusions, value)) RefreshEntropy(); }
    }

    public string Prefix
    {
        get => _prefix;
        set => SetProperty(ref _prefix, value);
    }

    public string Suffix
    {
        get => _suffix;
        set => SetProperty(ref _suffix, value);
    }

    // ---------------------------------------------------------------
    // Sample match (Pro mode)
    // ---------------------------------------------------------------

    /// <summary>Pasted sample key for analysis.</summary>
    public string SampleText
    {
        get => _sampleText;
        set => SetProperty(ref _sampleText, value);
    }

    /// <summary>The detected sample profile summary.</summary>
    public string SampleProfileText
    {
        get => _sampleProfileText;
        private set => SetProperty(ref _sampleProfileText, value);
    }

    // ---------------------------------------------------------------
    // Output
    // ---------------------------------------------------------------

    /// <summary>The current generated key.</summary>
    public GeneratedKey? CurrentKey
    {
        get => _currentKey;
        private set => SetProperty(ref _currentKey, value);
    }

    /// <summary>True when a key is displayed.</summary>
    public bool HasCurrentKey => _currentKey is not null;

    /// <summary>True when the current key is a key pair.</summary>
    public bool IsKeyPair => _currentKey?.IsKeyPair == true;

    /// <summary>Masked by default; revealed on toggle.</summary>
    public bool IsRevealed
    {
        get => _isRevealed;
        private set => SetProperty(ref _isRevealed, value);
    }

    /// <summary>The value to display (masked or raw).</summary>
    public string DisplayValue =>
        _currentKey is null ? string.Empty
        : _isRevealed ? _currentKey.Value
        : Mask(_currentKey.Value);

    /// <summary>The public half to display (key pairs only).</summary>
    public string DisplayPublicValue =>
        _currentKey?.PublicValue is null ? string.Empty
        : _isRevealed ? _currentKey.PublicValue
        : Mask(_currentKey.PublicValue);

    /// <summary>Entropy estimate for the current configuration.</summary>
    public double EntropyBits { get; private set; }

    /// <summary>Entropy band for the meter color.</summary>
    public EntropyLevel EntropyLevel { get; private set; }

    /// <summary>Entropy display label.</summary>
    public string EntropyLabel { get; private set; } = "—";

    /// <summary>Session history (last 5, newest first).</summary>
    public ObservableCollection<KeyMasterHistoryRowViewModel> History { get; }

    /// <summary>Keys from the last batch generation.</summary>
    public ObservableCollection<GeneratedKey> BatchResults { get; }

    /// <summary>True while generating.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>The status line.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>The error banner text (empty = hidden).</summary>
    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    /// <summary>True when the error banner should be visible.</summary>
    public bool HasError => !string.IsNullOrEmpty(_errorText);

    /// <summary>True when batch results are shown.</summary>
    public bool HasBatchResults => BatchResults.Count > 0;

    /// <summary>Keys generated this session.</summary>
    public int SessionCount => _store.SessionCount;

    // ---------------------------------------------------------------
    // Visibility flags (computed from SelectedProfile.Kind)
    // ---------------------------------------------------------------

    public bool ShowAlgorithm => AlgorithmOptions.Count > 1;
    public bool ShowCurve => _selectedProfile?.Kind is KeyKind.JwtEcdsa or KeyKind.GoEcdsa;
    public bool ShowModulus => _selectedProfile?.Kind is KeyKind.JwtRsa or KeyKind.DotNetRsa;
    public bool ShowLength => _selectedProfile is not null
        && _selectedProfile.Kind is not (KeyKind.JwtRsa or KeyKind.JwtEcdsa or KeyKind.DotNetRsa or KeyKind.GoEd25519);
    public bool ShowLengthOptions => LengthOptions is not null;
    public bool ShowProControls => _selectedProfile?.Kind == KeyKind.ProKey;
    public bool ShowSampleMatch => _selectedProfile?.Kind == KeyKind.ProKey;
    public bool ShowCharsetToggles => _selectedProfile?.Kind == KeyKind.ProKey;

    // ---------------------------------------------------------------
    // Commands
    // ---------------------------------------------------------------

    public ICommand GenerateCommand { get; }
    public ICommand GenerateBatchCommand { get; }
    public ICommand RegenerateCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand CopyPublicCommand { get; }
    public ICommand RevealCommand { get; }
    public ICommand AnalyzeSampleCommand { get; }
    public ICommand MatchSampleCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand SelectProfileCommand { get; }

    // ---------------------------------------------------------------
    // Internals
    // ---------------------------------------------------------------

    private static string[] AlgorithmOptionsFor(KeyKind kind) => kind switch
    {
        KeyKind.JwtHmac or KeyKind.DotNetHmac => new[] { "HS256", "HS384", "HS512" },
        KeyKind.JwtRsa => new[] { "RSA", "Pkcs1" },
        KeyKind.JwtEcdsa or KeyKind.GoEcdsa => new[] { "ECDSA" },
        KeyKind.GoEd25519 => new[] { "Ed25519" },
        KeyKind.DjangoSecret => new[] { "Django", "EnvSafe", "Raw" },
        KeyKind.FlaskSecret => new[] { "Session", "Api" },
        _ => new[] { string.Empty },
    };

    // KM-3: the formats each kind can actually emit. Anything else is blocked
    // in the UI (ComboBox is bound to FormatOptions) and rejected in
    // GenerateAsync with the error banner.
    private static KeyFormat[] FormatOptionsFor(KeyKind kind) => kind switch
    {
        KeyKind.JwtRsa or KeyKind.DotNetRsa or KeyKind.JwtEcdsa or KeyKind.GoEcdsa =>
            new[] { KeyFormat.Pem, KeyFormat.Json },
        KeyKind.GoEd25519 => new[] { KeyFormat.Pem, KeyFormat.Json, KeyFormat.Hex },
        KeyKind.ProKey => Enum.GetValues<KeyFormat>(),
        KeyKind.DjangoSecret => new[] { KeyFormat.Raw, KeyFormat.Hex, KeyFormat.Base64, KeyFormat.Base64Url },
        _ => new[] { KeyFormat.Raw, KeyFormat.Hex, KeyFormat.Base64, KeyFormat.Base64Url },
    };

    private void RefreshVisibility()
    {
        OnPropertyChanged(nameof(ShowAlgorithm));
        OnPropertyChanged(nameof(ShowCurve));
        OnPropertyChanged(nameof(ShowModulus));
        OnPropertyChanged(nameof(ShowLength));
        OnPropertyChanged(nameof(ShowLengthOptions));
        OnPropertyChanged(nameof(ShowProControls));
        OnPropertyChanged(nameof(ShowSampleMatch));
        OnPropertyChanged(nameof(ShowCharsetToggles));
    }

    private void RefreshLengthBounds()
    {
        if (_selectedProfile is null) return;
        var min = _selectedProfile.MinChars > 0 ? _selectedProfile.MinChars
            : _selectedProfile.MinBytes > 0 ? _selectedProfile.MinBytes
            : 8;
        if (_length < min) _length = min;
        if (_length > _selectedProfile.MaxLength) _length = _selectedProfile.MaxLength;
        OnPropertyChanged(nameof(Length));
    }

    private void RefreshEntropy()
    {
        if (_selectedProfile is null)
        {
            EntropyBits = 0;
            EntropyLevel = EntropyLevel.Critical;
            EntropyLabel = "—";
            NotifyEntropy();
            return;
        }

        var estimate = EstimateEntropy();
        EntropyBits = estimate.Bits;
        EntropyLevel = estimate.Level;
        EntropyLabel = estimate.Label;
        NotifyEntropy();
    }

    private void NotifyEntropy()
    {
        OnPropertyChanged(nameof(EntropyBits));
        OnPropertyChanged(nameof(EntropyLevel));
        OnPropertyChanged(nameof(EntropyLabel));
    }

    private EntropyEstimate EstimateEntropy()
    {
        var kind = _selectedProfile!.Kind;
        return kind switch
        {
            KeyKind.ProKey => EntropyEstimate.FromPool(
                _length, ProGenerator.BuildCharset(BuildSpec()).Length),
            KeyKind.DjangoSecret => EntropyEstimate.FromPool(
                Math.Max(_length, DjangoSecretGenerator.MinChars),
                DjangoSecretGenerator.CharsetFor(_algorithm).Length),
            KeyKind.JwtHmac or KeyKind.DotNetHmac or KeyKind.GoHmac or KeyKind.JavaHmac =>
                EntropyEstimate.FromBytes(Math.Max(
                    _length, SymmetricSecretGenerator.MinBytesFor(_algorithm, 32))),
            KeyKind.FastApiSecret => EntropyEstimate.FromBytes(Math.Max(_length, 32)),
            KeyKind.FlaskSecret => EntropyEstimate.FromBytes(Math.Max(_length, 24)),
            KeyKind.GoAesGcm => EntropyEstimate.FromBytes(_length),
            KeyKind.JwtRsa or KeyKind.DotNetRsa => EntropyEstimate.FromBytes(_modulusBits / 8),
            KeyKind.JwtEcdsa or KeyKind.GoEcdsa => EntropyEstimate.FromBytes(
                _curve == "P-521" ? 66 : _curve == "P-384" ? 48 : 32),
            KeyKind.GoEd25519 => EntropyEstimate.FromBytes(32),
            _ => EntropyEstimate.FromBytes(_length),
        };
    }

    private GenerationSpec BuildSpec() => new(
        _selectedProfile!.Kind,
        _length,
        _algorithm,
        _curve,
        _modulusBits,
        _format,
        _useUpper, _useLower, _useDigits, _useSymbols,
        _exclusions, _prefix, _suffix, 1);

    private async Task GenerateAsync(int count)
    {
        if (_selectedProfile is null || IsBusy) return;
        if (!IsFormatSupported)
        {
            ErrorText = $"Format {_format} is not supported for {_selectedProfile.DisplayName}. " +
                $"Choose one of: {string.Join(", ", FormatOptions)}.";
            StatusText = "Generation blocked: unsupported format.";
            return;
        }
        ErrorText = string.Empty;
        IsBusy = true;
        try
        {
            var spec = BuildSpec();
            if (count <= 1)
            {
                var key = await _registry.GenerateAsync(spec).ConfigureAwait(true);
                _store.Add(key);
                CurrentKey = key;
                IsRevealed = false;
                BatchResults.Clear();
                OnPropertyChanged(nameof(HasBatchResults));
                StatusText = $"Generated {key.ProfileLabel}. Session: {_store.SessionCount}.";
            }
            else
            {
                var keys = await _registry.GenerateBatchAsync(spec, count).ConfigureAwait(true);
                foreach (var k in keys) _store.Add(k);
                BatchResults.Clear();
                foreach (var k in keys) BatchResults.Add(k);
                CurrentKey = keys[^1];
                IsRevealed = false;
                StatusText = $"Generated {keys.Count} keys. Session: {_store.SessionCount}.";
                OnPropertyChanged(nameof(HasBatchResults));
            }
            OnPropertyChanged(nameof(DisplayValue));
            OnPropertyChanged(nameof(DisplayPublicValue));
            OnPropertyChanged(nameof(HasCurrentKey));
            OnPropertyChanged(nameof(IsKeyPair));
            OnPropertyChanged(nameof(SessionCount));
            RefreshHistory();
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            StatusText = "Generation failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshHistory()
    {
        History.Clear();
        foreach (var k in _store.History)
        {
            History.Add(new KeyMasterHistoryRowViewModel(k, _copyToClipboard));
        }
    }

    private void ToggleReveal()
    {
        IsRevealed = !_isRevealed;
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(DisplayPublicValue));
    }

    private async Task CopyAsync(bool publicHalf)
    {
        var value = publicHalf ? _currentKey?.PublicValue : _currentKey?.Value;
        if (string.IsNullOrEmpty(value))
        {
            StatusText = "Nothing to copy yet.";
            return;
        }
        if (_copyToClipboard is null)
        {
            StatusText = "Copy is not available in this build.";
            return;
        }
        await _copyToClipboard(value).ConfigureAwait(true);
        StatusText = publicHalf ? "Public key copied." : "Key copied to clipboard.";
    }

    private void AnalyzeSample()
    {
        var profile = SampleKeyAnalyzer.Analyze(_sampleText);
        SampleProfileText = profile?.Summary ?? "No sample to analyze.";
    }

    private void MatchSample()
    {
        var profile = SampleKeyAnalyzer.Analyze(_sampleText);
        if (profile is null || _selectedProfile?.Kind != KeyKind.ProKey)
        {
            StatusText = "Paste a sample key first (Pro mode only).";
            return;
        }

        _length = Math.Clamp(profile.Length, 8, 256);
        _useUpper = profile.HasUpper;
        _useLower = profile.HasLower;
        _useDigits = profile.HasDigits;
        _useSymbols = profile.HasSymbols;

        OnPropertyChanged(nameof(Length));
        OnPropertyChanged(nameof(UseUpper));
        OnPropertyChanged(nameof(UseLower));
        OnPropertyChanged(nameof(UseDigits));
        OnPropertyChanged(nameof(UseSymbols));
        RefreshEntropy();
        StatusText = $"Matched sample: {profile.Summary}. Review and generate.";
    }

    private void Clear()
    {
        CurrentKey = null;
        IsRevealed = false;
        BatchResults.Clear();
        OnPropertyChanged(nameof(HasBatchResults));
        _store.Clear();
        History.Clear();
        ErrorText = string.Empty;
        StatusText = "Cleared. Session history emptied.";
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(DisplayPublicValue));
        OnPropertyChanged(nameof(HasCurrentKey));
        OnPropertyChanged(nameof(IsKeyPair));
        OnPropertyChanged(nameof(SessionCount));
    }

    private static string Mask(string value) =>
        new('•', Math.Min(24, Math.Max(8, value.Length)));
}
