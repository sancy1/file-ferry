// ============================================================================
// FILE:     src/Prompt.Core/Domain/PromptPatch.cs
// PURPOSE:  A declarative PATCH of a prompt. Each field is a PatchField
//           that distinguishes "leave unchanged" from "set to null/value".
//           This replaces the delegate-based Action<Prompt> PATCH (F-001):
//           a declarative patch is retry-safe because it is idempotent.
// RELATION: Consumed by IPromptStore.PatchAsync and SqlitePromptStore.PatchAsync
//           (P1.3). Replaces the delegate-based API that the plan text
//           described.
// NOTES:    - Every mutable field of Prompt has a PatchField here.
//           - IsFavorite is PatchField<bool> (not PatchField<bool?>) - the
//             field is non-nullable; "leave unchanged" is expressed via
//             IsSpecified.
//           - Tags is PatchField<IReadOnlyList<string>> - a tag list
//             replaces the whole set; there is no per-tag patch semantics.
// ============================================================================

namespace Prompt.Core.Domain;

/// <summary>
/// A declarative PATCH of a prompt. Each field says whether to leave the
/// field unchanged or set it to the given value.
/// </summary>
public sealed record PromptPatch(
    PatchField<string> Title,
    PatchField<string> Body,
    PatchField<BodyFormat> Format,
    PatchField<Guid?> CategoryId,
    PatchField<Guid?> SubCategoryId,
    PatchField<string?> TargetModel,
    PatchField<bool> IsFavorite,
    PatchField<IReadOnlyList<string>> Tags);