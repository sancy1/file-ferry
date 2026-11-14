// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/PromptCategoryRowViewModel.cs
// PURPOSE:  One row in the sidebar's category tree. Represents a category, a
//           sub-category, or the display-only "Uncategorized" pseudo-node.
// RELATION: Built by PromptListViewModel (P2.3) from the category and
//           sub-category stores. Bound to the TreeView in PromptListPanel.axaml.
// NOTES:    - The pseudo-node is display-only. It is not a category record,
//             is not editable, and is not deletable. It appears only when at
//             least one prompt has CategoryId == null (F-128).
//           - Every real category and sub-category is deletable and editable
//             by the user at P2.7 (the plan's P1.2 cascade ensures prompts
//             survive deletion).
// ============================================================================

using Prompt.Core.Domain;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// A category, sub-category, or the "Uncategorized" pseudo-node in the tree.
/// </summary>
public sealed class PromptCategoryRowViewModel : ViewModelBase
{
    private bool _isExpanded;

    /// <summary>
    /// Creates a category node with the given sub-category children.
    /// </summary>
    public PromptCategoryRowViewModel(
        Category category,
        IReadOnlyList<PromptCategoryRowViewModel> children,
        int promptCount)
    {
        Category = category;
        SubCategory = null;
        Children = children;
        PromptCount = promptCount;
        _isExpanded = true;
    }

    /// <summary>
    /// Creates a sub-category node. Sub-categories are leaves.
    /// </summary>
    public PromptCategoryRowViewModel(SubCategory subCategory, int promptCount)
    {
        Category = null;
        SubCategory = subCategory;
        Children = Array.Empty<PromptCategoryRowViewModel>();
        PromptCount = promptCount;
    }

    /// <summary>
    /// Creates the display-only "Uncategorized" pseudo-node.
    /// </summary>
    public static PromptCategoryRowViewModel Uncategorized(int promptCount) =>
        new(promptCount);

    private PromptCategoryRowViewModel(int promptCount)
    {
        Category = null;
        SubCategory = null;
        Children = Array.Empty<PromptCategoryRowViewModel>();
        PromptCount = promptCount;
        IsPseudo = true;
    }

    /// <summary>The category record, or null for sub-categories and the pseudo-node.</summary>
    public Category? Category { get; }

    /// <summary>The sub-category record, or null for categories and the pseudo-node.</summary>
    public SubCategory? SubCategory { get; }

    /// <summary>True for the display-only "Uncategorized" node.</summary>
    public bool IsPseudo { get; }

    /// <summary>The child sub-categories. Empty for sub-categories and the pseudo-node.</summary>
    public IReadOnlyList<PromptCategoryRowViewModel> Children { get; }

    /// <summary>The number of prompts in this node (or under it for categories).</summary>
    public int PromptCount { get; }

    /// <summary>The label shown in the tree: "Name (Count)".</summary>
    public string DisplayName
    {
        get
        {
            var name = Category?.Name
                ?? SubCategory?.Name
                ?? "Uncategorized";
            return $"{name} ({PromptCount})";
        }
    }

    /// <summary>True when the tree node is expanded.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}