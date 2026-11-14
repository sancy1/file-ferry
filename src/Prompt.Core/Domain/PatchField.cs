// ============================================================================
// FILE:     src/Prompt.Core/Domain/PatchField.cs
// PURPOSE:  A wrapper for a field in a PATCH operation. Distinguishes "not
//           specified - leave unchanged" from "specified - set to this
//           value (which may be null)". This is the F-001 correction.
// RELATION: Referenced by PromptPatch. Consumed by SqlitePromptStore.PatchAsync
//           (P1.3).
// NOTES:    - The record has only its positional constructor. Static
//             factory members on a generic type violate CA1000 (F-082), so
//             there are none. Call sites use new PatchField<T>(isSpecified,
//             value) directly.
//           - IsSpecified = false means "leave the field alone".
//           - IsSpecified = true and Value = null means "set the field to
//             null".
//           - IsSpecified = true and Value = X means "set the field to X".
// ============================================================================

namespace Prompt.Core.Domain;

/// <summary>
/// A single field in a PATCH operation. <see cref="IsSpecified"/> = false
/// leaves the field unchanged; <see cref="IsSpecified"/> = true sets the
/// field to <see cref="Value"/> (which may be null).
/// </summary>
/// <typeparam name="T">The field type.</typeparam>
/// <param name="IsSpecified">False means leave unchanged.</param>
/// <param name="Value">The value to set when specified.</param>
public sealed record PatchField<T>(bool IsSpecified, T? Value);