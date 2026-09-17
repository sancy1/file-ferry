// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/OsKindConverters.cs
// PURPOSE:  IValueConverter instances for conditional XAML based on the
//           detected OS. Used to hide Windows-style caption buttons on
//           macOS (where native traffic lights appear instead).
// RELATION: Referenced from MainWindow.axaml via x:Static. Consumes
//           FileHarvester.App.Platform.OsKind. No other dependencies.
// ============================================================================

using System.Globalization;
using Avalonia.Data.Converters;
using FileHarvester.App.Platform;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// Static converter instances for OS-based visibility.
/// </summary>
public static class OsKindConverters
{
    /// <summary>
    /// True when the OS is NOT macOS. Use this to show custom caption
    /// buttons on Windows and Linux, and hide them on macOS where the
    /// system provides native traffic lights.
    /// </summary>
    public static readonly IValueConverter IsNotMacOS =
        new FuncValueConverter<OsKind, bool>(os => os != OsKind.MacOS);
}
