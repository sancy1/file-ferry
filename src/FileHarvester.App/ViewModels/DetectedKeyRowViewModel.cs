// ============================================================================
// filepath: src/FileHarvester.App/ViewModels/DetectedKeyRowViewModel.cs
// layer:    App / ViewModels
// module:   FileHarvester.App.ViewModels
// since:    Phase A (KeyForge)
// purpose:  One row in the KeyForge detected-keys panel. Shows the family
//           badge, the masked original, the visible fake, and a per-key copy
//           command.
// RELATIONSHIPS
//   Implements : FileHarvester.App.ViewModels.ViewModelBase
//   Used by    : KeyForgeViewModel.DetectedKeys; KeyForgePage.axaml
//   See also   : KeyForgeViewModel
// ============================================================================

using System.Windows.Input;
using DeploymentVault.Core.KeyForge.Domain;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// A single detected key row in the KeyForge detected-keys panel.
/// </summary>
public sealed class DetectedKeyRowViewModel : ViewModelBase
{
    private readonly Func<string, Task>? _copyToClipboard;

    /// <summary>Builds the row view model.</summary>
    /// <param name="key">The detected key (original + fake). Not null.</param>
    /// <param name="copyToClipboard">Clipboard delegate for the per-key copy. Optional.</param>
    public DetectedKeyRowViewModel(DetectedKey key, Func<string, Task>? copyToClipboard = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        Key = key;
        _copyToClipboard = copyToClipboard;
        CopyCommand = new AsyncRelayCommand<object>(_ => CopyAsync());
    }

    /// <summary>The underlying detected key.</summary>
    public DetectedKey Key { get; }

    /// <summary>The key family.</summary>
    public KeyFamily Family => Key.Family;

    /// <summary>The family display label.</summary>
    public string FamilyLabel => Key.Family switch
    {
        KeyFamily.PrefixedToken => "Prefixed",
        KeyFamily.Jwt => "JWT",
        KeyFamily.Base64Blob => "Base64",
        KeyFamily.Uuid => "UUID",
        KeyFamily.PemBlock => "PEM",
        _ => "Generic",
    };

    /// <summary>The masked original (prefix + bullets). The real key is never shown in full.</summary>
    public string OriginalMasked => Mask(Key.Original);

    /// <summary>The shape-identical fake (visible, since it is fake).</summary>
    public string Fake => Key.Fake;

    /// <summary>Copies the fake to the clipboard.</summary>
    public ICommand CopyCommand { get; }

    private static string Mask(string original)
    {
        if (original.Length <= 4) return new string('•', original.Length);
        var prefix = original[..Math.Min(4, original.Length)];
        var bullets = new string('•', Math.Min(8, original.Length - prefix.Length));
        return prefix + bullets;
    }

    private async Task CopyAsync()
    {
        if (_copyToClipboard is null) return;
        await _copyToClipboard(Key.Fake).ConfigureAwait(true);
    }
}
