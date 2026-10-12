// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/FieldSpec.cs
// PURPOSE:  Schema for one credential field in a provider profile form.
//           Drives the dynamic credential-form engine introduced in C.2.
// RELATION: Consumed by ProviderProfile. Rendered by the dynamic form
//           engine in the App layer. FieldKind selects the UI control.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// The kind of UI control a credential field renders as.
/// </summary>
/// <remarks>
/// Text: a single-line text box.
/// Password: a masked single-line text box.
/// Multiline: a multi-line text area (connection strings, JSON).
/// File: a file picker (service account JSON).
/// Choice: a dropdown (region, endpoint family).
/// </remarks>
public enum FieldKind
{
    /// <summary>A single-line text box.</summary>
    Text = 0,

    /// <summary>A masked single-line text box.</summary>
    Password = 1,

    /// <summary>A multi-line text area.</summary>
    Multiline = 2,

    /// <summary>A file picker.</summary>
    File = 3,

    /// <summary>A dropdown.</summary>
    Choice = 4,
}

/// <summary>
/// Schema for one credential field.
/// </summary>
/// <param name="Key">
/// The field key. Stable; used to look up the entered value.
/// </param>
/// <param name="Label">The user-visible label.</param>
/// <param name="Kind">The UI control kind.</param>
/// <param name="Required">True when the field must be provided.</param>
/// <param name="Placeholder">Optional placeholder text.</param>
/// <param name="HelpText">Optional help text shown under the field.</param>
/// <param name="ValidatePattern">
/// Optional regex the value must match. Null means no pattern check.
/// </param>
public sealed record FieldSpec(
    string Key,
    string Label,
    FieldKind Kind,
    bool Required,
    string? Placeholder,
    string? HelpText,
    string? ValidatePattern);
