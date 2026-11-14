// ============================================================================
// FILE:     src/ImageTextify.Core/Abstractions/IImageTextifyIntakeService.cs
// PURPOSE:  Contract for the intake evaluation step. Owns duplicate
//           detection and validator orchestration; returns a decision
//           record. Contains no UI state.
// RELATION: Implemented by ImageTextifyIntakeService in ImageTextify.Platform.
//           Consumed by the intake queue in the ImageTextify workspace
//           view model. The validator is invoked through this service so
//           the queue never depends on the validator directly.
// ============================================================================

namespace ImageTextify.Core.Abstractions;

using ImageTextify.Core.Domain;

/// <summary>
/// Evaluates a candidate file for inclusion in the intake queue.
/// </summary>
/// <remarks>
/// <para>
/// The service is stateless and thread-safe. It holds no queue; the
/// caller passes the currently queued paths on every call. Duplicate
/// detection is the service's responsibility; the validator itself
/// checks one file only and does not know about the queue.
/// </para>
/// <para>
/// The service never touches Avalonia, ObservableCollection, or the
/// ImageTextify workspace view model. Its only inputs are a file path,
/// a category, the current queue paths, and a cancellation token.
/// </para>
/// <para>
/// Duplicate rule. A file is a duplicate when its full path already
/// exists in the queue for the same category. The same path with a
/// different category is accepted. Path comparison is
/// case-insensitive on Windows and case-sensitive on POSIX, matching
/// the underlying filesystem's default. The comparison rule mirrors
/// the validator's rule so the two agree.
/// </para>
/// </remarks>
public interface IImageTextifyIntakeService
{
    /// <summary>
    /// Evaluates one candidate file.
    /// </summary>
    /// <param name="fullPath">
    /// The absolute path of the candidate file. Not null or empty.
    /// </param>
    /// <param name="category">
    /// The category whose extension allowlist applies.
    /// </param>
    /// <param name="existingFullPathsWithCategory">
    /// The full paths currently in the queue for the same category.
    /// May be empty; not null.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the evaluation. The service honors the token and returns
    /// a <see cref="ImageTextifyIntakeReason.Rejected"/> decision whose
    /// validation reason is <see cref="ImageTextifyValidationReason.Cancelled"/>,
    /// rather than throwing.
    /// </param>
    /// <returns>
    /// The intake decision. Always non-null.
    /// </returns>
    Task<ImageTextifyIntakeDecision> EvaluateAsync(
        string fullPath,
        ImageTextifyCategory category,
        IReadOnlyCollection<string> existingFullPathsWithCategory,
        CancellationToken cancellationToken);
}