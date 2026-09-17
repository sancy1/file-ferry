// ============================================================================
// FILE:     src/FileHarvester.App/Services/LogoService.cs
// PURPOSE:  Resolves the single embedded app logo to an avares:// URI for
//           XAML binding. Checks in priority order and returns the first
//           one that exists. If none is embedded, returns null and the
//           title bar shows text only (no icon).
// RELATION: Consumed by MainWindowViewModel, which exposes it as a bound
//           property to MainWindow.axaml. Depends on Avalonia's asset
//           loader (AssetLoader.Exists) to verify the resource is present
//           at runtime, so the same code works whether the app is running
//           from source, from a published .exe, or from an installed
//           Start Menu shortcut.
// ============================================================================

using Avalonia.Platform;

namespace FileHarvester.App.Services;

/// <summary>
/// Resolves the embedded app logo. One logo per app - the first file
/// present in priority order wins.
/// </summary>
public static class LogoService
{
    /// <summary>
    /// Candidate logo URIs in priority order. The first one that exists
    /// as an embedded AvaloniaResource is returned.
    /// </summary>
    private static readonly string[] Candidates =
    {
        "avares://FileHarvester.App/Assets/file-ferry-logo.png",
        "avares://FileHarvester.App/Assets/logo.png",
        "avares://FileHarvester.App/Assets/logo.ico",
    };

    /// <summary>
    /// The resolved logo URI, or null when no logo is embedded.
    /// </summary>
    public static Uri? CurrentUri { get; } = Resolve();

    /// <summary>
    /// True when a logo is available. False when the app is running
    /// without any embedded logo (title bar will show text only).
    /// </summary>
    public static bool HasLogo => CurrentUri is not null;

    private static Uri? Resolve()
    {
        foreach (var uri in Candidates)
        {
            try
            {
                var parsed = new Uri(uri);

                // Ask Avalonia whether this asset actually exists in the
                // embedded resources. Returns false for the missing ones.
                if (AssetLoader.Exists(parsed))
                {
                    return parsed;
                }
            }
            catch (UriFormatException)
            {
                // Malformed candidate: skip. This cannot happen with the
                // hardcoded list above, but the guard keeps the loop
                // safe if the list is ever edited.
            }
        }

        return null;
    }
}
