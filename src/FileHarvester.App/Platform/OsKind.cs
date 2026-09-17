// ============================================================================
// FILE:     src/FileHarvester.App/Platform/OsKind.cs
// PURPOSE:  Runtime OS detection. Exposes an OsKind enum and a static
//           Current property that is populated once at startup.
// RELATION: Consumed by App.axaml.cs (to configure Avalonia platform
//           options) and by AppTitleBar.axaml (to switch caption button
//           placement, insets, and Mica/Acrylic vs. CSD styling).
//           No #if directives — the same source compiles on all three OSes.
// ============================================================================

namespace FileHarvester.App.Platform;

/// <summary>
/// The three desktop operating systems File-Ferry targets.
/// </summary>
public enum OsKind
{
    Windows,
    MacOS,
    Linux,
}

/// <summary>
/// Static OS detection. Populated once at process start and never changes.
/// </summary>
public static class Os
{
    /// <summary>
    /// The current operating system.
    /// </summary>
    public static OsKind Current { get; } = Detect();

    private static OsKind Detect()
    {
        if (OperatingSystem.IsWindows()) return OsKind.Windows;
        if (OperatingSystem.IsMacOS())   return OsKind.MacOS;
        return OsKind.Linux;
    }
}
