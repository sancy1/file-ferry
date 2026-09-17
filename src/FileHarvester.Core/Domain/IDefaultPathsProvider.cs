// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/IDefaultPathsProvider.cs
// PURPOSE:  Contract for OS-specific standard folder access. Keeps the
//           Core library free of #if WINDOWS / #if OSX directives.
// RELATION: Implemented in Phase 5.1 (WindowsPaths, MacPaths, LinuxPaths).
//           Consumed by FileResolver (Phase 1.1) to try common locations
//           before falling back to whole-PC search, and by OutputSink
//           (Phase 6.0) to locate the Downloads folder for the
//           auto-fallback destination.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// Provides access to the OS's standard user folders. Every path returned
/// is absolute, exists (or is creatable), and uses the OS-native
/// directory separator.
/// </summary>
public interface IDefaultPathsProvider
{
    /// <summary>
    /// The current user's Downloads folder. On Windows:
    /// <c>%USERPROFILE%\Downloads</c>. On macOS: <c>~/Downloads</c>. On
    /// Linux: <c>$XDG_DOWNLOAD_DIR</c>, falling back to <c>~/Downloads</c>.
    /// </summary>
    string DownloadsFolder { get; }

    /// <summary>
    /// The current user's Desktop folder.
    /// </summary>
    string DesktopFolder { get; }

    /// <summary>
    /// The current user's Documents folder.
    /// </summary>
    string DocumentsFolder { get; }

    /// <summary>
    /// The current user's home directory.
    /// </summary>
    string UserProfileFolder { get; }

    /// <summary>
    /// The directory where File-Ferry stores its per-user configuration
    /// and logs. On Windows: <c>%APPDATA%\FileHarvester</c>. On macOS:
    /// <c>~/Library/Application Support/FileHarvester</c>. On Linux:
    /// <c>$XDG_CONFIG_HOME/FileHarvester</c>.
    /// </summary>
    string AppDataFolder { get; }

    /// <summary>
    /// The directories the resolver tries first when a bare filename is
    /// not found in the hinted directory. Typically: the app's working
    /// directory, Desktop, Documents, Downloads. Order matters — the
    /// resolver tries them in sequence.
    /// </summary>
    IReadOnlyList<string> CommonSearchRoots { get; }
}
