// ============================================================================
// FILE:     src/Prompt.Core/Domain/ISubCategoryStore.cs
// PURPOSE:  Contract for CRUD operations on sub-categories. Deleting a
//           sub-category sets its prompts' SubCategoryId to null (never
//           deletes prompts) via the schema's ON DELETE SET NULL foreign key.
// RELATION: Implemented by SqliteCategoryStore (P1.2). Consumed by
//           PromptListViewModel (P2.3) and CategoryManagerViewModel (P2.7).
// NOTES:    ListAsync accepts an optional categoryId filter for the tree
//           view. Same resilience wrapping as ICategoryStore.
// ============================================================================

namespace Prompt.Core.Domain;

/// <summary>
/// CRUD operations on sub-categories.
/// </summary>
public interface ISubCategoryStore
{
    /// <summary>Creates a new sub-category and returns it.</summary>
    Task<SubCategory> CreateAsync(SubCategory subCategory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists sub-categories, ordered by SortOrder then Name. When
    /// <paramref name="categoryId"/> is not null, only sub-categories
    /// belonging to that category are returned.
    /// </summary>
    Task<IReadOnlyList<SubCategory>> ListAsync(Guid? categoryId = null, CancellationToken cancellationToken = default);

    /// <summary>Gets a sub-category by id, or null if it does not exist.</summary>
    Task<SubCategory?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Replaces a sub-category's fields (PUT semantics).</summary>
    Task<SubCategory> UpdateAsync(SubCategory subCategory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a sub-category. Its prompts have SubCategoryId set to null
    /// (via the schema's ON DELETE SET NULL) and are preserved.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}