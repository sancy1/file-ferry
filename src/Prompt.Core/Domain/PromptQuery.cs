// ============================================================================
// FILE:     src/Prompt.Core/Domain/PromptQuery.cs
// PURPOSE:  A search/filter request against the prompt store. Combines a
//           free-text query (FTS5-matched) with organizational filters,
//           tag filters, a favorites toggle, and pagination.
// RELATION: Referenced by IPromptStore.SearchAsync (P1.5). Consumed by
//           PromptSearchBarViewModel (P2.8). Uses PromptScope from this
//           namespace.
// NOTES:    Null or empty Text means "no text filter" (search all). Null
//           Tags means "no tag filter". Skip/Take are caller-supplied; there
//           is no default page size at the record level. IncludeDeleted
//           exposes soft-deleted prompts when true (used by the trash view).
// ============================================================================

namespace Prompt.Core.Domain;

/// <summary>
/// A search and filter request against the prompt store.
/// </summary>
/// <remarks>
/// Every nullable field means "no filter on this dimension." Combinations are
/// ANDed: a query with a text, a category, and a tag returns prompts that
/// match all three.
/// </remarks>
public sealed record PromptQuery(
    string? Text,
    PromptScope Scope,
    Guid? CategoryId,
    Guid? SubCategoryId,
    IReadOnlyList<string>? Tags,
    bool FavoritesOnly,
    bool IncludeDeleted,
    int Skip,
    int Take);