// ============================================================================
// FILE:     src/FileHarvester.App/Views/ImageTextifyDropHelpers.cs
// PURPOSE:  Pure helpers used by the ImageTextify page code-behind. The
//           drag-and-drop path extraction is separated from the event
//           handlers so the null, empty, and whitespace filtering can be
//           unit-tested without a real OS drag.
// RELATION: Consumed by ImageTextifyPage.axaml.cs. Visible to the test
//           assembly via InternalsVisibleTo.
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using Avalonia.Platform.Storage;

namespace FileHarvester.App.Views;

/// <summary>
/// Pure helpers for the ImageTextify drag-and-drop code-behind.
/// </summary>
internal static class ImageTextifyDropHelpers
{
    /// <summary>
    /// Extracts local file paths from a nullable array of storage items,
    /// discarding nulls, non-file items, and paths that are null, empty,
    /// or whitespace.
    /// </summary>
    /// <param name="items">
    /// The storage items returned by a drop, or null when the transfer
    /// carried no files.
    /// </param>
    /// <returns>
    /// A new list of local paths, in the order the items appeared.
    /// Never null; empty when no valid paths were found.
    /// </returns>
    internal static List<string> ExtractLocalPaths(IStorageItem[]? items)
    {
        if (items is null || items.Length == 0)
        {
            return new List<string>();
        }

        return items
            .Select(item => item.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToList();
    }
}