// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/CodeViewerConverters.cs
// PURPOSE:  Small value converters for the CodeViewer Project Browser
//           rows: the expand/collapse arrow glyph and the folder/file
//           icon glyph.
// ============================================================================

using System.Globalization;
using Avalonia.Data.Converters;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// Converters used by the CodeViewer browser item template.
/// </summary>
public static class CodeViewerConverters
{
    /// <summary>Maps a directory's expanded state to ▸ / ▾.</summary>
    public static readonly IValueConverter ExpandArrow = new FuncValueConverter<bool, string>(
        expanded => expanded ? "▾" : "▸");

    /// <summary>Maps a node's directory flag to a folder / file glyph.</summary>
    public static readonly IValueConverter NodeIcon = new FuncValueConverter<bool, string>(
        isDirectory => isDirectory ? "📁" : "📄");
}
