// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/PromptListRowViewModel.cs
// PURPOSE:  One row in the sidebar's prompt list. Holds the Prompt record
//           and exposes the display fields the list item binds to.
// RELATION: Built by PromptListViewModel (P2.3). Bound to the ListBox in
//           PromptListPanel.axaml.
// NOTES:    - The favorite indicator is a star (filled if IsFavorite, outline
//             otherwise). The star character is chosen in the XAML, not here.
//           - No editing here. The row is read-only; editing opens the
//             editor dialog at P2.5.
// ============================================================================

using Prompt.Core.Domain;
using PromptRecord = Prompt.Core.Domain.Prompt;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// One row in the prompt list. The underlying <see cref="Prompt"/> plus
/// display helpers.
/// </summary>
public sealed class PromptListRowViewModel
{
    /// <summary>
    /// Wraps the given prompt for display.
    /// </summary>
    /// <param name="prompt">The prompt record. Never null.</param>
    public PromptListRowViewModel(PromptRecord prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        Prompt = prompt;
    }

    /// <summary>The underlying prompt record.</summary>
    public PromptRecord Prompt { get; }

    /// <summary>The title shown in the list. Matches <see cref="Prompt.Title"/>.</summary>
    public string Title => Prompt.Title;

    /// <summary>True when the prompt is favorited (filled star in the list).</summary>
    public bool IsFavorite => Prompt.IsFavorite;

    /// <summary>The tooltip shown on hover: the category name or "Uncategorized".</summary>
    public string ToolTipText => Prompt.CategoryId is null
        ? "Uncategorized"
        : "Category: " + Prompt.CategoryId;
}