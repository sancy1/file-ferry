// ============================================================================
// FILE:     src/Prompt.Core/Domain/PromptScope.cs
// PURPOSE:  Enumerates the search scope a PromptQuery applies. Determines
//           whether the query searches all prompts or only those within a
//           specific category or sub-category.
// RELATION: Referenced by PromptQuery. Consumed by SqlitePromptStore.SearchAsync
//           (P1.5). No logic.
// NOTES:    The scope is applied as a WHERE predicate on the search; it does
//           not override the text query. A scoped text search combines
//           FTS5 MATCH with a category/sub-category predicate.
// ============================================================================

namespace Prompt.Core.Domain;

/// <summary>
/// The scope a <see cref="PromptQuery"/> applies to its search.
/// </summary>
public enum PromptScope
{
    /// <summary>All prompts across all categories.</summary>
    All = 0,

    /// <summary>Prompts within a single category (including its sub-categories).</summary>
    Category = 1,

    /// <summary>Prompts within a single sub-category only.</summary>
    SubCategory = 2,
}