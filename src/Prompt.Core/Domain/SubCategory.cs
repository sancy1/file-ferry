// ============================================================================
// FILE:     src/Prompt.Core/Domain/SubCategory.cs
// PURPOSE:  A second-level container for prompts, belonging to exactly one
//           Category. Prompts may be saved to a sub-category instead of the
//           parent category.
// RELATION: Referenced by Prompt (Prompt.SubCategoryId) and Category
//           (Category.Id). Consumed by ICategoryStore (P1.2). No logic.
// NOTES:    A sub-category's CategoryId is set at creation and never null.
//           Deleting a sub-category sets its prompts' SubCategoryId to null
//           (never deletes prompts).
// ============================================================================

namespace Prompt.Core.Domain;

/// <summary>
/// A second-level container for prompts, belonging to exactly one
/// <see cref="Category"/>.
/// </summary>
/// <remarks>
/// A sub-category may hold prompts directly. Deleting a sub-category sets its
/// prompts' <see cref="Prompt.SubCategoryId"/> to <c>null</c> and preserves
/// them.
/// </remarks>
public sealed record SubCategory(
    Guid Id,
    Guid CategoryId,
    string Name,
    string? Description,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);