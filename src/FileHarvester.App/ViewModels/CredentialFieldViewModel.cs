// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/CredentialFieldViewModel.cs
// PURPOSE:  One credential field rendered by the dynamic credential form.
//           Bound to a single FieldSpec; holds the entered value and its
//           per-field validation message.
// RELATION: Constructed by CloudCredentialFormViewModel (one per FieldSpec
//           in the profile). Rendered by CloudCredentialDialog.axaml.
//           FieldKind selects the UI control.
// ============================================================================

using System.Text.RegularExpressions;
using FileHarvester.Core.Domain;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The view model for one credential field in the dynamic credential form.
/// </summary>
/// <remarks>
/// The entered value is held in a private backing field and exposed only
/// through <see cref="Value"/>. No public member of this type displays,
/// logs, serializes, or throws the value. The password and secret fields
/// are masked in the view; the value is never echoed elsewhere.
///
/// The type is UI-only: it holds no Avalonia types, opens no dialogs,
/// performs no I/O, and uses no resilience policy.
/// </remarks>
public sealed class CredentialFieldViewModel : ViewModelBase
{
    private string _value = string.Empty;
    private string? _validationMessage;

    /// <summary>
    /// Initializes a new instance of the <see cref="CredentialFieldViewModel"/> class
    /// from the supplied field schema.
    /// </summary>
    /// <param name="spec">The field schema. Must not be null.</param>
    public CredentialFieldViewModel(FieldSpec spec)
    {
        Spec = spec;
        Key = spec.Key;
        Label = spec.Label;
        Kind = spec.Kind;
        IsRequired = spec.Required;
        Placeholder = spec.Placeholder;
        HelpText = spec.HelpText;
    }

    /// <summary>Gets the field schema this field was built from.</summary>
    public FieldSpec Spec { get; }

    /// <summary>Gets the stable field key.</summary>
    public string Key { get; }

    /// <summary>Gets the user-visible label.</summary>
    public string Label { get; }

    /// <summary>Gets the UI control kind.</summary>
    public FieldKind Kind { get; }

    /// <summary>Gets a value indicating whether the field must be provided.</summary>
    public bool IsRequired { get; }

    /// <summary>Gets the optional placeholder text.</summary>
    public string? Placeholder { get; }

    /// <summary>Gets the optional help text shown under the field.</summary>
    public string? HelpText { get; }

    /// <summary>
    /// Gets or sets the entered value. May be a secret; handle in memory only.
    /// </summary>
    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    /// <summary>
    /// Gets or sets the per-field validation message. Null or empty when the
    /// field has no validation error to display.
    /// </summary>
    public string? ValidationMessage
    {
        get => _validationMessage;
        set
        {
            if (SetProperty(ref _validationMessage, value))
            {
                OnPropertyChanged(nameof(HasValidationMessage));
            }
        }
    }

    /// <summary>Gets a value indicating whether the control renders as multi-line.</summary>
    public bool IsMultiline => Kind == FieldKind.Multiline;

    /// <summary>Gets a value indicating whether the control renders as a masked password.</summary>
    public bool IsPassword => Kind == FieldKind.Password;

    /// <summary>Gets a value indicating whether the control renders as a file picker.</summary>
    public bool IsFile => Kind == FieldKind.File;

    /// <summary>
    /// Gets a value indicating whether the field is a Choice kind.
    /// In Step 4 a Choice field renders as a plain text box because
    /// FieldSpec has no allowed-values member (F-080).
    /// </summary>
    public bool IsChoice => Kind == FieldKind.Choice;

    /// <summary>
    /// Gets a value indicating whether the field renders as a plain single-line text box.
    /// A Choice field also renders as a plain text box in Step 4 (F-080).
    /// </summary>
    public bool IsPlainText =>
        Kind == FieldKind.Text || Kind == FieldKind.Choice;

    /// <summary>Gets a value indicating whether HelpText is present and non-empty.</summary>
    public bool HasHelpText => !string.IsNullOrEmpty(HelpText);

    /// <summary>Gets a value indicating whether a validation message is present and non-empty.</summary>
    public bool HasValidationMessage =>
        !string.IsNullOrEmpty(ValidationMessage);

    /// <summary>
    /// Clears the entered value and the validation message. Called on Cancel,
    /// on window close, and after the caller has consumed a successful result.
    /// </summary>
    /// <summary>
    /// Validates this field: required completeness, then the optional
    /// <see cref="FieldSpec.ValidatePattern"/> when a value is present.
    /// Sets <see cref="ValidationMessage"/> and returns the result.
    /// </summary>
    /// <returns>True when the field passes; otherwise false.</returns>
    public bool Validate()
    {
        if (IsRequired && string.IsNullOrWhiteSpace(Value))
        {
            ValidationMessage = "This field is required.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(Value)
            && !string.IsNullOrWhiteSpace(Spec.ValidatePattern))
        {
            var pattern = Spec.ValidatePattern!;
            if (!Regex.IsMatch(Value, pattern))
            {
                ValidationMessage = "The value does not match the expected format.";
                return false;
            }
        }

        ValidationMessage = null;
        return true;
    }

    public void Clear()
    {
        Value = string.Empty;
        ValidationMessage = null;
    }
}
