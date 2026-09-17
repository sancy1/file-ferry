// ============================================================================
// FILE:     src\FileHarvester.App/ViewModels/FormatOptionViewModel.cs
// PURPOSE:  One entry in the Output Options panel's format dropdown. Wraps
//           the metadata a format writer exposes: display name, extension,
//           MIME type. Bound by Views/OutputOptionsPanel.axaml.
// RELATION: Created by OutputOptionsViewModel from registered IFormatWriter
//           instances (Phase 4). In Phase 3.3 the list is populated with
//           placeholder entries; Phase 4 replaces them with real writers.
// ============================================================================

namespace FileHarvester.App.ViewModels;

/// <summary>
/// One format option in the Output Options dropdown.
/// </summary>
public sealed class FormatOptionViewModel
{
    /// <summary>
    /// Creates a format option.
    /// </summary>
    /// <param name="extension">File extension including the dot, e.g. ".md".</param>
    /// <param name="displayName">Human-readable name, e.g. "Markdown".</param>
    /// <param name="mimeType">MIME type, e.g. "text/markdown".</param>
    public FormatOptionViewModel(
        string extension,
        string displayName,
        string mimeType)
    {
        Extension = extension ?? throw new ArgumentNullException(nameof(extension));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        MimeType = mimeType ?? throw new ArgumentNullException(nameof(mimeType));
    }

    /// <summary>
    /// File extension including the leading dot.
    /// </summary>
    public string Extension { get; }

    /// <summary>
    /// Human-readable name for the dropdown.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// MIME type.
    /// </summary>
    public string MimeType { get; }

    /// <summary>
    /// Text shown in the dropdown: "Markdown (.md)".
    /// </summary>
    public string DisplayText => $"{DisplayName} ({Extension})";
}
