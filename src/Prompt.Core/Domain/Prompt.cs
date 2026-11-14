// ============================================================================
// FILE:     src/Prompt.Core/Domain/Prompt.cs
// PURPOSE:  The stored representation of a single prompt. Fields are the
//           union of identity, body, organization, metadata, and lifecycle.
//           The record is a DTO: no validation, no rendering, no I/O.
// RELATION: Referenced by ICategoryStore (category linkage) and IPromptStore
//           (all CRUD). Consumed by the exporter adapter (P3.1) via
//           PromptDocumentAdapter. Reuses BodyFormat from this namespace.
// NOTES:    Body is stored as the string the user typed (per BodyFormat).
//           Rendering is the viewer's job (P2.4); format-specific adaptation
//           is the exporter's job (P3.1). Tags are user-supplied strings; they
//           are not normalized here. The nullable fields (CategoryId,
//           SubCategoryId, TargetModel) are the ones PromptPatch (F-001)
//           wraps as PatchField<T> at P1.3 to distinguish "set to null" from
//           "leave unchanged".
// ============================================================================

namespace Prompt.Core.Domain;

/// <summary>
/// A stored prompt: title, body, organizational linkage, metadata, lifecycle.
/// </summary>
/// <remarks>
/// This is a pure DTO. It performs no validation, no rendering, and no I/O.
/// The <see cref="CategoryId"/>, <see cref="SubCategoryId"/>, and
/// <see cref="TargetModel"/> fields are nullable so that a prompt may be
/// uncategorized, unsub-categorized, or provider-agnostic. Those three
/// nullable fields are the ones <c>PromptPatch</c> (F-001) wraps as
/// <c>PatchField&lt;T&gt;</c> at P1.3.
/// </remarks>
public sealed record Prompt(
    Guid Id,
    string Title,
    string Body,
    BodyFormat Format,
    Guid? CategoryId,
    Guid? SubCategoryId,
    string? TargetModel,
    bool IsFavorite,
    bool IsDeleted,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);