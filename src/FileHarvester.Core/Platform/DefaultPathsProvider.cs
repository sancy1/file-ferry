// ============================================================================
// FILE:     src/FileHarvester.Core/Platform/DefaultPathsProvider.cs
// PURPOSE:  Cross-platform implementation of IDefaultPathsProvider using
//           the .NET Environment.GetFolderPath API. Works on Windows,
//           macOS, and Linux without any OS-specific code.
// RELATION: Registered by AddFileHarvester() in Phase 3.0. Consumed by
//           FileResolver (Phase 1.1) and OutputSink (Phase 6.0). May be
//           replaced by OS-specific implementations in Phase 5.1 if the
//           defaults need tuning per platform.
// ============================================================================

using FileHarvester.Core.Domain;

namespace FileHarvester.Core.Platform;

/// <summary>
/// Provides standard user folders using the .NET runtime's cross-platform
/// folder resolution.
/// </summary>
public sealed class DefaultPathsProvider : IDefaultPathsProvider
{
    /// <inheritdoc/>
    public string DownloadsFolder { get; }

    /// <inheritdoc/>
    public string DesktopFolder { get; }

    /// <inheritdoc/>
    public string DocumentsFolder { get; }

    /// <inheritdoc/>
    public string UserProfileFolder { get; }

    /// <inheritdoc/>
    public string AppDataFolder { get; }

    /// <inheritdoc/>
    public IReadOnlyList<string> CommonSearchRoots { get; }

    /// <summary>
    /// Resolves every standard folder at construction time. The values do
    /// not change during the process lifetime.
    /// </summary>
    public DefaultPathsProvider()
    {
        UserProfileFolder = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);

        DownloadsFolder = SafeGetFolder(
            Environment.SpecialFolder.UserProfile,
            "Downloads");

        DesktopFolder = SafeGetFolder(
            Environment.SpecialFolder.DesktopDirectory,
            fallbackSubfolder: null);

        DocumentsFolder = SafeGetFolder(
            Environment.SpecialFolder.MyDocuments,
            fallbackSubfolder: null);

        AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileHarvester");

        // The order here matters: the resolver tries these roots in
        // sequence when a relative path or bare filename is not found
        // through the hinted directory. We prioritize locations where
        // user-supplied files typically live.
        var roots = new List<string>
        {
            AppContext.BaseDirectory,
            DesktopFolder,
            DocumentsFolder,
            DownloadsFolder,
        };

        CommonSearchRoots = roots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string SafeGetFolder(
        Environment.SpecialFolder folder,
        string? fallbackSubfolder)
    {
        var basePath = Environment.GetFolderPath(folder);

        if (string.IsNullOrWhiteSpace(basePath))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(fallbackSubfolder))
        {
            return basePath;
        }

        return Path.Combine(basePath, fallbackSubfolder);
    }
}
