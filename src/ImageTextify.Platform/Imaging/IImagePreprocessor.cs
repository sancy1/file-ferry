// ============================================================================
// FILE:     src/ImageTextify.Platform/Imaging/IImagePreprocessor.cs
// PURPOSE:  The preprocessing entry point. Decodes an image stream and
//           returns two named variants, original and enhanced, plus the
//           diagnostics of what the pipeline did.
// RELATION: Implemented by ImagePreprocessor. Consumed by the OCR engine
//           in Phase 2.4b. Phase 2.4b owns the resilience wrapping of the
//           whole extraction operation.
// ============================================================================

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// Prepares an image for OCR.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deterministic.</b> The same input stream and the same options always
/// produce the same Enhanced variant bytes. There is no randomness and no
/// model inference.
/// </para>
/// <para>
/// <b>Variant ownership.</b> Both variants are returned as disposable
/// handles. The caller owns them and must dispose them.
/// </para>
/// <para>
/// <b>Effective DPI.</b> The pipeline never trusts file DPI metadata. It
/// uses the caller hint when supplied; otherwise it estimates 200 DPI for
/// images wider than 1000 pixels and 150 DPI otherwise. Upscaling is
/// attempted only when the effective DPI is below
/// <see cref="ImagePreprocessingOptions.UpscaleThresholdDpi"/>, and the
/// scale factor is capped at
/// <see cref="ImagePreprocessingOptions.MaxUpscaleFactor"/>.
/// </para>
/// <para>
/// <b>Resilience.</b> The preprocessor does not call
/// Portfolio.Resilience directly. The Phase 2.4b caller wraps the stream
/// read, preprocessing, and OCR inference in the image-textify-ocr policy.
/// Preprocessing propagates the cancellation token and disposes every
/// allocated bitmap in a finally block on cancellation.
/// </para>
/// <para>
/// <b>Limits.</b> Preprocessing cannot recover detail destroyed by blur.
/// It improves scale, contrast, noise, and skew only.
/// </para>
/// </remarks>
public interface IImagePreprocessor
{
    /// <summary>
    /// Decodes the image stream and produces the Original and Enhanced
    /// variants.
    /// </summary>
    /// <param name="imageStream">
    /// The image bytes. The caller owns the stream; the preprocessor does
    /// not close it. Null is rejected.
    /// </param>
    /// <param name="options">
    /// Preprocessing options. Not null.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the pipeline. On cancellation the pipeline throws
    /// <see cref="OperationCanceledException"/> and disposes every
    /// allocated bitmap.
    /// </param>
    /// <returns>
    /// The two variants and the diagnostics of what the pipeline did.
    /// </returns>
    Task<ImagePreprocessingResult> PreprocessAsync(
        Stream imageStream,
        ImagePreprocessingOptions options,
        CancellationToken cancellationToken);
}