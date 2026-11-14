// ============================================================================
// FILE:     src/Prompt.Core/Domain/ICategoryStore.cs
// PURPOSE:  Contract for CRUD operations on top-level categories. Deleting
//           a category sets its prompts' CategoryId to null (never deletes
//           prompts) via the schema's ON DELETE SET NULL foreign key.
// RELATION: Implemented by SqliteCategoryStore (P1.2). Consumed by
//           PromptListViewModel (P2.3) and CategoryManagerViewModel (P2.7).
// NOTES:    All operations are wrapped in the prompt-store resilience policy
//           by the implementation. The interface does not declare resilience
//           concerns; that is the implementation's responsibility.
// ============================================================================

namespace Prompt.Core.Domain;

/// <summary>
/// CRUD operations on top-level categories.
/// </summary>
public interface ICategoryStore
{
    /// <summary>Creates a new category and returns it.</summary>
    Task<Category> CreateAsync(Category category, CancellationToken cancellationToken = default);

    /// <summary>Lists all categories, ordered by SortOrder then Name.</summary>
    Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets a category by id, or null if it does not exist.</summary>
    Task<Category?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a category's fields (PUT semantics).
    /// </summary>
    Task<Category> UpdateAsync(Category category, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a category. Its prompts have CategoryId set to null
    /// (via the schema's ON DELETE SET NULL) and are preserved.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}