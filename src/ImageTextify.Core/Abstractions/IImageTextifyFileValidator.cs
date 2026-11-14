// ============================================================================
// FILE:     src/ImageTextify.Core/Abstractions/IImageTextifyFileValidator.cs
// PURPOSE:  Contract for intake-time file eligibility validation. Existence
//           check runs through Portfolio.Resilience (PolicyNames.FileRead);
//           extension check runs against a per-category allowlist. Does not
//           read file contents.
// RELATION: Implemented by ImageTextifyFileValidator in ImageTextify.Platform.
//           Consumed by the intake queue in Phase 1.5b.
// ============================================================================

namespace ImageTextify.Core.Abstractions;

using ImageTextify.Core.Domain;

/// <summary>
/// Validates a single file for intake eligibility.
/// </summary>
/// <remarks>
/// <para>
/// Validation covers exactly two things: that the file exists (checked
/// through Portfolio.Resilience using the existing file-read policy) and
/// that its extension is on the allowlist for the supplied category. It
/// does not open, read, hash, decode, or otherwise touch the file's
/// contents. Content and signature validation belong to a later,
/// separately approved phase.
/// </para>
/// <para>
/// The validator validates one file at a time. It does not receive or
/// inspect the intake queue. Duplicate detection is the queue's
/// responsibility, not the validator's.
/// </para>
/// </remarks>
public interface IImageTextifyFileValidator
{
    /// <summary>
    /// Validates the file at <paramref name="fullPath"/> for the given category.
    /// </summary>
    /// <param name="fullPath">
    /// The absolute path of the file to validate. Not null or empty. This
    /// value is redacted before it is exposed for display or logging.
    /// </param>
    /// <param name="category">
    /// The category whose extension allowlist applies.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the validation. The validator honors the token and returns a
    /// <see cref="ImageTextifyValidationReason.Cancelled"/> result rather
    /// than throwing.
    /// </param>
    /// <returns>
    /// A result carrying <see cref="ImageTextifyValidationResult.IsValid"/>,
    /// the reason, the display name, and the (unredacted) full path. Use
    /// <see cref="ImageTextifyValidationResult.RedactedPath"/> for any log or
    /// diagnostic output.
    /// </returns>
    Task<ImageTextifyValidationResult> ValidateAsync(
        string fullPath,
        ImageTextifyCategory category,
        CancellationToken cancellationToken);
}

/// <summary>
/// The outcome of an intake validation.
/// </summary>
/// <param name="IsValid">
/// True when the file exists and its extension is on the category
/// allowlist. False for every other outcome.
/// </param>
/// <param name="Reason">
/// The reason the outcome was reached. See
/// <see cref="ImageTextifyValidationReason"/>.
/// </param>
/// <param name="DisplayName">
/// The file name without directory components. Safe to display and log.
/// </param>
/// <param name="FullPath">
/// The absolute path supplied to the validator. Not safe to log without
/// redaction; use <see cref="RedactedPath"/> for that purpose.
/// </param>
/// <param name="SizeBytes">
/// The file size in bytes at the moment of validation. Zero when
/// the file is missing, unreadable, or the size cannot be determined.
/// Never throws; a failed size read is treated as zero.
/// </param>
public sealed record ImageTextifyValidationResult(
    bool IsValid,
    ImageTextifyValidationReason Reason,
    string DisplayName,
    string FullPath,
    long SizeBytes)
{
    /// <summary>
    /// The full path with the user segment redacted, suitable for logging.
    /// </summary>
    public string RedactedPath =>
        ImageTextify.Core.Diagnostics.ImageTextifyRedaction.RedactPath(FullPath);
}

/// <summary>
/// The reason an intake validation returned its result.
/// </summary>
public enum ImageTextifyValidationReason
{
    /// <summary>The file exists and its extension is on the category allowlist.</summary>
    Valid = 0,

    /// <summary>The file does not exist at the supplied path.</summary>
    MissingFile = 1,

    /// <summary>The file's extension is not on the category allowlist.</summary>
    UnsupportedExtension = 2,

    /// <summary>
    /// The existence check could not be completed. The policy fallback
    /// treats this as fail-closed and returns this reason.
    /// </summary>
    Unavailable = 3,

    /// <summary>
    /// The caller cancelled. The result is safe and non-throwing.
    /// </summary>
    Cancelled = 4,

    /// <summary>
    /// The supplied category is not yet supported for this file type.
    /// Reserved for future use; not returned in Phase 1.5a.
    /// </summary>
    NotYetSupported = 5,
}
