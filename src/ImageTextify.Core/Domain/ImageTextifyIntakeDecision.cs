// ============================================================================
// FILE:     src/ImageTextify.Core/Domain/ImageTextifyIntakeDecision.cs
// PURPOSE:  The outcome of an intake evaluation for one candidate file.
//           Carries whether the file should be added to the queue, the
//           underlying validation result, and the intake reason.
// RELATION: Returned by IImageTextifyIntakeService.EvaluateAsync.
//           Consumed by the intake queue in the ImageTextify workspace
//           view model.
// ============================================================================

namespace ImageTextify.Core.Domain;

using ImageTextify.Core.Abstractions;

/// <summary>
/// The outcome of an intake evaluation for one candidate file.
/// </summary>
/// <param name="ShouldAdd">
/// True only when the file should be added to the queue. False for a
/// duplicate or a rejected file. A false value is not an error: the
/// caller still has a complete <see cref="Validation"/> to display.
/// </param>
/// <param name="Validation">
/// The underlying validation result. Always present, even when
/// <see cref="ShouldAdd"/> is false, so the caller can render a
/// meaningful row or warning.
/// </param>
/// <param name="Reason">
/// The intake-level reason for the decision. See
/// <see cref="ImageTextifyIntakeReason"/>.
/// </param>
public sealed record ImageTextifyIntakeDecision(
    bool ShouldAdd,
    ImageTextifyValidationResult Validation,
    ImageTextifyIntakeReason Reason);

/// <summary>
/// The intake-level reason for a decision.
/// </summary>
public enum ImageTextifyIntakeReason
{
    /// <summary>The file passed validation and is not a duplicate.</summary>
    Accepted = 0,

    /// <summary>
    /// A file with the same full path and the same category is already in
    /// the queue.
    /// </summary>
    Duplicate = 1,

    /// <summary>
    /// The file failed validation: missing, unsupported extension,
    /// unavailable, cancelled, or not yet supported.
    /// </summary>
    Rejected = 2,
}