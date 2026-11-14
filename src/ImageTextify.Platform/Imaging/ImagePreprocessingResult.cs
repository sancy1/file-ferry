// ============================================================================
// FILE:     src/ImageTextify.Platform/Imaging/ImagePreprocessingResult.cs
// PURPOSE:  The two variants produced by preprocessing, plus the diagnostics
//           of what the pipeline did. Owns both bitmap handles and disposes
//           them together. A type that owns disposable resources implements
//           IDisposable.
// RELATION: Returned by IImagePreprocessor.PreprocessAsync. The caller
//           disposes the result, which disposes both handles.
// ============================================================================

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// The result of the preprocessing pipeline.
/// </summary>
/// <remarks>
/// <para>
/// The result owns both <see cref="SkiaBitmapHandle"/> instances. Disposing
/// the result disposes both handles. Disposal is idempotent.
/// </para>
/// <para>
/// The caller owns the result and must dispose it. In production code this
/// is achieved with a <c>using</c> statement; in tests, with a
/// <c>using var</c> declaration.
/// </para>
/// </remarks>
public sealed class ImagePreprocessingResult : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Creates the result. Both handles are owned by the result.
    /// </summary>
    /// <param name="originalVariant">
    /// The variant label for the original. Always
    /// <see cref="ImageVariant.Original"/>.
    /// </param>
    /// <param name="enhancedVariant">
    /// The variant label for the enhanced bitmap. Always
    /// <see cref="ImageVariant.Enhanced"/>.
    /// </param>
    /// <param name="original">
    /// The decoded, unmodified bitmap. Not null. The result takes ownership.
    /// </param>
    /// <param name="enhanced">
    /// The bitmap produced by the pipeline. Not null. The result takes
    /// ownership.
    /// </param>
    /// <param name="diagnostics">
    /// What the pipeline did. Not null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any parameter is null.
    /// </exception>
    public ImagePreprocessingResult(
        ImageVariant originalVariant,
        ImageVariant enhancedVariant,
        SkiaBitmapHandle original,
        SkiaBitmapHandle enhanced,
        ImagePreprocessingDiagnostics diagnostics)
    {
        OriginalVariant = originalVariant;
        EnhancedVariant = enhancedVariant;
        Original = original ?? throw new ArgumentNullException(nameof(original));
        Enhanced = enhanced ?? throw new ArgumentNullException(nameof(enhanced));
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>
    /// The variant label for <see cref="Original"/>. Always
    /// <see cref="ImageVariant.Original"/>.
    /// </summary>
    public ImageVariant OriginalVariant { get; }

    /// <summary>
    /// The variant label for <see cref="Enhanced"/>. Always
    /// <see cref="ImageVariant.Enhanced"/>.
    /// </summary>
    public ImageVariant EnhancedVariant { get; }

    /// <summary>
    /// The decoded, unmodified bitmap. Owned by this result.
    /// </summary>
    public SkiaBitmapHandle Original { get; }

    /// <summary>
    /// The bitmap produced by the pipeline. Owned by this result.
    /// </summary>
    public SkiaBitmapHandle Enhanced { get; }

    /// <summary>
    /// What the pipeline did. Never contains image content or user text.
    /// </summary>
    public ImagePreprocessingDiagnostics Diagnostics { get; }

    /// <summary>
    /// Disposes both owned handles. Idempotent.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Dispose both handles. Each handle's own Dispose is idempotent,
        // so a double call here is harmless.
        try { Original.Dispose(); } catch { }
        try { Enhanced.Dispose(); } catch { }
    }
}
