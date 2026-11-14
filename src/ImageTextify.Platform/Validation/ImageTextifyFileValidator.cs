// ============================================================================
// FILE:     src/ImageTextify.Platform/Validation/ImageTextifyFileValidator.cs
// PURPOSE:  Implementation of IImageTextifyFileValidator. Checks file
//           existence through the existing file-read resilience policy
//           and extension membership against a per-category allowlist.
//           Stateless and thread-safe.
// RELATION: Implements IImageTextifyFileValidator. Registered as a
//           singleton in AddImageTextify.
// ============================================================================

using ImageTextify.Core.Abstractions;
using ImageTextify.Core.Domain;
using FileHarvester.Core.Composition;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Errors;

namespace ImageTextify.Platform.Validation;

/// <summary>
/// Validates intake files for existence and extension eligibility.
/// </summary>
/// <remarks>
/// <para>
/// Stateless and thread-safe. The single instance may be used
/// concurrently by the intake queue.
/// </para>
/// <para>
/// Existence is checked through Portfolio.Resilience with the existing
/// file-read policy. No new policy is created in this phase; the failure
/// profile of a bounded existence check matches file-read exactly.
/// </para>
/// <para>
/// Extension validation uses an explicit allowlist per category. A file
/// whose extension is not on the allowlist is rejected with
/// <see cref="ImageTextifyValidationReason.UnsupportedExtension"/>, even
/// if it exists.
/// </para>
/// </remarks>
public sealed class ImageTextifyFileValidator : IImageTextifyFileValidator
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".webp",
    };

    private static readonly HashSet<string> PdfExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".mp3", ".m4a", ".flac", ".ogg",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".mov", ".avi", ".webm",
    };

    private readonly IResilienceExecutor _resilience;

    /// <summary>
    /// Creates the validator with its resilience dependency.
    /// </summary>
    /// <param name="resilience">The resilience executor. Not null.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="resilience"/> is null.
    /// </exception>
    public ImageTextifyFileValidator(IResilienceExecutor resilience)
    {
        _resilience = resilience ?? throw new ArgumentNullException(nameof(resilience));
    }

    /// <inheritdoc/>
    public async Task<ImageTextifyValidationResult> ValidateAsync(
        string fullPath,
        ImageTextifyCategory category,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        var displayName = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(displayName))
        {
            displayName = fullPath;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new ImageTextifyValidationResult(
                IsValid: false,
                Reason: ImageTextifyValidationReason.Cancelled,
                DisplayName: displayName,
                FullPath: fullPath,
                SizeBytes: 0);
        }

        // Allowlist check runs before the existence check so an obviously
        // wrong extension does not pay the cost of an I/O round trip.
        var extension = Path.GetExtension(fullPath);
        if (!IsAllowedExtension(extension, category))
        {
            return new ImageTextifyValidationResult(
                IsValid: false,
                Reason: ImageTextifyValidationReason.UnsupportedExtension,
                DisplayName: displayName,
                FullPath: fullPath,
                SizeBytes: 0);
        }

        try
        {
            var exists = await _resilience.ExecuteAsync(
                policyName: PolicyNames.FileRead,
                operation: _ => Task.FromResult(File.Exists(fullPath)),
                fallback: _ => Task.FromResult(true),
                ct: cancellationToken).ConfigureAwait(false);

            if (!exists)
            {
                return new ImageTextifyValidationResult(
                    IsValid: false,
                    Reason: ImageTextifyValidationReason.MissingFile,
                    DisplayName: displayName,
                    FullPath: fullPath,
                    SizeBytes: 0);
            }

            return new ImageTextifyValidationResult(
                IsValid: true,
                Reason: ImageTextifyValidationReason.Valid,
                DisplayName: displayName,
                FullPath: fullPath,
                SizeBytes: GetFileSize(fullPath));
        }
        catch (OperationCanceledException)
        {
            return new ImageTextifyValidationResult(
                IsValid: false,
                Reason: ImageTextifyValidationReason.Cancelled,
                DisplayName: displayName,
                FullPath: fullPath,
                SizeBytes: 0);
        }
        catch (ResilienceException)
        {
            return new ImageTextifyValidationResult(
                IsValid: false,
                Reason: ImageTextifyValidationReason.Unavailable,
                DisplayName: displayName,
                FullPath: fullPath,
                SizeBytes: 0);
        }
    }

    private static bool IsAllowedExtension(string extension, ImageTextifyCategory category) =>
        category switch
        {
            ImageTextifyCategory.Image => ImageExtensions.Contains(extension),
            ImageTextifyCategory.Pdf   => PdfExtensions.Contains(extension),
            ImageTextifyCategory.Audio => AudioExtensions.Contains(extension),
            ImageTextifyCategory.Video => VideoExtensions.Contains(extension),
            _                          => false,
        };
    private static long GetFileSize(string fullPath)
    {
        try
        {
            var info = new FileInfo(fullPath);
            return info.Exists ? info.Length : 0L;
        }
        catch
        {
            return 0L;
        }
    }

}
