// ============================================================================
// FILE:     src/Prompt.Core/Domain/Category.cs
// PURPOSE:  A top-level organizational container for prompts. A category
//           may contain prompts directly or sub-categories that contain them.
// RELATION: Referenced by Prompt (Prompt.CategoryId) and SubCategory
//           (SubCategory.CategoryId). Consumed by ICategoryStore (P1.2). No
//           logic.
// NOTES:    Deleting a category sets its prompts' CategoryId to null (never
//           deletes prompts). SortOrder is a user-declared display order;
//           ties are broken by Name.
// ============================================================================

namespace Prompt.Core.Domain;

/// <summary>
/// A top-level organizational container for prompts.
/// </summary>
/// <remarks>
/// A category may hold prompts directly, or hold sub-categories that hold
/// prompts. Deleting a category is a non-destructive operation: its prompts
/// have their <see cref="Prompt.CategoryId"/> set to <c>null</c>.
/// </remarks>
public sealed record Category(
    Guid Id,
    string Name,
    string? Description,
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);