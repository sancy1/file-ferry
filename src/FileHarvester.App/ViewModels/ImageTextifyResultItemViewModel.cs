// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/ImageTextifyResultItemViewModel.cs
// PURPOSE:  One extraction result row for the ImageTextify workspace. Holds
//           the display name, the extracted text, the joined quality warnings,
//           and a per-row status line. Immutable: the extraction command
//           creates one instance per queued file and the UI binds read-only.
// RELATION: Created by ImageTextifyViewModel.ExtractAllAsync, one per accepted
//           queue row. Bound in a later increment by ImageTextifyPage.axaml.
//           The type never reads the file system, never touches Avalonia, and
//           never holds a stream or an engine reference.
// ============================================================================

namespace FileHarvester.App.ViewModels;

/// <summary>
/// One extraction result row. Immutable; the extraction command constructs
/// one instance per processed queue row and the UI reads it without mutation.
/// </summary>
public sealed class ImageTextifyResultItemViewModel
{
    /// <summary>
    /// Creates the result row.
    /// </summary>
    /// <param name="displayName">
    /// The source file name, copied from the queue row. Not null.
    /// </param>
    /// <param name="extractedText">
    /// The extracted text. Empty on failure; never null.
    /// </param>
    /// <param name="warningsText">
    /// The engine quality warnings joined by semicolons, or empty. Never null.
    /// </param>
    /// <param name="statusText">
    /// The per-row status: Extracted, Failed with the reason, or Cancelled.
    /// Not null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any argument is null.
    /// </exception>
    public ImageTextifyResultItemViewModel(
        string displayName,
        string extractedText,
        string warningsText,
        string statusText)
    {
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        ExtractedText = extractedText ?? throw new ArgumentNullException(nameof(extractedText));
        WarningsText = warningsText ?? throw new ArgumentNullException(nameof(warningsText));
        StatusText = statusText ?? throw new ArgumentNullException(nameof(statusText));
    }

    /// <summary>The source file name, copied from the queue row.</summary>
    public string DisplayName { get; }

    /// <summary>The extracted text. Empty on failure.</summary>
    public string ExtractedText { get; }

    /// <summary>The engine quality warnings joined by semicolons, or empty.</summary>
    public string WarningsText { get; }

    /// <summary>The per-row status line: Extracted, Failed with reason, or Cancelled.</summary>
    public string StatusText { get; }
}
