// ============================================================================
// FILE:     src/ImageTextify.Platform/Intake/ImageTextifyIntakeService.cs
// PURPOSE:  Implementation of IImageTextifyIntakeService. Runs the
//           validator, then applies the duplicate rule, then returns
//           the intake decision. Stateless and thread-safe.
// RELATION: Implements IImageTextifyIntakeService. Registered as a
//           singleton in AddImageTextify. Depends on
//           IImageTextifyFileValidator only.
// ============================================================================

using ImageTextify.Core.Abstractions;
using ImageTextify.Core.Domain;

namespace ImageTextify.Platform.Intake;

/// <summary>
/// Evaluates candidate files for intake: validation first, then the
/// duplicate rule.
/// </summary>
/// <remarks>
/// <para>
/// Stateless and thread-safe. The single instance may be used
/// concurrently by the intake queue.
/// </para>
/// <para>
/// Order of evaluation. Validation runs first: a file that does not
/// exist or whose extension is not on the category allowlist is
/// rejected with the validator's own reason and is never checked
/// against the queue. Only a valid file is compared against the
/// existing queue paths. This order matches the validator's own
/// order (existence before extension) and matches user expectation:
/// an invalid file is invalid regardless of whether it is also a
/// duplicate.
/// </para>
/// <para>
/// Comparison rule. Paths are compared using the same rule the
/// validator uses for existence. On Windows, ordinal-ignore-case. On
/// POSIX, ordinal.
/// </para>
/// </remarks>
public sealed class ImageTextifyIntakeService : IImageTextifyIntakeService
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private readonly IImageTextifyFileValidator _validator;

    /// <summary>
    /// Creates the service with its validator dependency.
    /// </summary>
    /// <param name="validator">The file validator. Not null.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="validator"/> is null.
    /// </exception>
    public ImageTextifyIntakeService(IImageTextifyFileValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    /// <inheritdoc/>
    public async Task<ImageTextifyIntakeDecision> EvaluateAsync(
        string fullPath,
        ImageTextifyCategory category,
        IReadOnlyCollection<string> existingFullPathsWithCategory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentNullException.ThrowIfNull(existingFullPathsWithCategory);

        var validation = await _validator
            .ValidateAsync(fullPath, category, cancellationToken)
            .ConfigureAwait(false);

        if (!validation.IsValid)
        {
            return new ImageTextifyIntakeDecision(
                ShouldAdd: false,
                Validation: validation,
                Reason: ImageTextifyIntakeReason.Rejected);
        }

        foreach (var existing in existingFullPathsWithCategory)
        {
            if (string.Equals(existing, fullPath, PathComparison))
            {
                return new ImageTextifyIntakeDecision(
                    ShouldAdd: false,
                    Validation: validation,
                    Reason: ImageTextifyIntakeReason.Duplicate);
            }
        }

        return new ImageTextifyIntakeDecision(
            ShouldAdd: true,
            Validation: validation,
            Reason: ImageTextifyIntakeReason.Accepted);
    }
}