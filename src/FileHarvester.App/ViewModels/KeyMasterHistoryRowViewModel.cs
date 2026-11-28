// ============================================================================
// filepath: src/FileHarvester.App/ViewModels/KeyMasterHistoryRowViewModel.cs
// layer:    App / ViewModels
// module:   FileHarvester.App.ViewModels
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  One row in the KeyMaster session-history list. Wraps a
//           GeneratedKey and adds per-row reveal/copy commands so the user
//           can inspect and copy any historical key without regenerating.
// RELATIONSHIPS
//   Implements : FileHarvester.App.ViewModels.ViewModelBase
//   Used by    : KeyMasterViewModel.History
//   See also   : GeneratedKey (the wrapped domain record)
// NOTES
//   - Avalonia-free. The clipboard is an injected delegate.
//   - Reveal is per-row and independent of the main output reveal.
// ============================================================================

using System.Windows.Input;
using DeploymentVault.Core.KeyMaster.Domain;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// A single session-history entry with reveal and copy commands.
/// </summary>
public sealed class KeyMasterHistoryRowViewModel : ViewModelBase
{
    private readonly Func<string, Task>? _copyToClipboard;
    private bool _isRevealed;

    /// <summary>Wraps a generated key for display.</summary>
    /// <param name="key">The generated key. Not null.</param>
    /// <param name="copyToClipboard">Clipboard delegate. Optional.</param>
    public KeyMasterHistoryRowViewModel(GeneratedKey key, Func<string, Task>? copyToClipboard = null)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        _copyToClipboard = copyToClipboard;
        RevealCommand = new RelayCommand(ToggleReveal);
        CopyCommand = new AsyncRelayCommand<object>(_ => CopyAsync());
        CopyPublicCommand = new AsyncRelayCommand<object>(_ => CopyPublicAsync());
    }

    /// <summary>The wrapped generated key.</summary>
    public GeneratedKey Key { get; }

    /// <summary>The key kind.</summary>
    public string Kind => Key.Kind.ToString();

    /// <summary>Short descriptor, e.g. "HS256 · 32 bytes".</summary>
    public string ProfileLabel => Key.ProfileLabel;

    /// <summary>Generation timestamp (UTC).</summary>
    public DateTimeOffset GeneratedAtUtc => Key.GeneratedAtUtc;

    /// <summary>True when this entry is a key pair.</summary>
    public bool IsKeyPair => Key.IsKeyPair;

    /// <summary>True when the raw value is shown.</summary>
    public bool IsRevealed
    {
        get => _isRevealed;
        private set
        {
            if (SetProperty(ref _isRevealed, value))
            {
                OnPropertyChanged(nameof(DisplayValue));
                OnPropertyChanged(nameof(RevealLabel));
            }
        }
    }

    /// <summary>The value to display: masked by default, raw when revealed.</summary>
    public string DisplayValue => _isRevealed ? Key.Value : Key.Masked;

    /// <summary>Label for the reveal toggle button.</summary>
    public string RevealLabel => _isRevealed ? "Hide" : "Reveal";

    /// <summary>Toggles masked/raw display.</summary>
    public ICommand RevealCommand { get; }

    /// <summary>Copies the raw private/secret value to the clipboard.</summary>
    public ICommand CopyCommand { get; }

    /// <summary>Copies the public half to the clipboard (key pairs only).</summary>
    public ICommand CopyPublicCommand { get; }

    private void ToggleReveal() => IsRevealed = !IsRevealed;

    private async Task CopyAsync()
    {
        if (_copyToClipboard is not null)
        {
            await _copyToClipboard(Key.Value).ConfigureAwait(true);
        }
    }

    private async Task CopyPublicAsync()
    {
        if (_copyToClipboard is not null && Key.PublicValue is not null)
        {
            await _copyToClipboard(Key.PublicValue).ConfigureAwait(true);
        }
    }
}
