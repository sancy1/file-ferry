// ============================================================================
// FILE:     src/ImageTextify.Platform/Imaging/Preprocess/IImageStage.cs
// PURPOSE:  The internal contract each preprocessing stage implements.
//           Exists so the stages are individually testable. Not part of any
//           public surface; the only public entry point is IImagePreprocessor.
// RELATION: Implemented by Orientation, Deskew, Denoise, Contrast, Upscale,
//           Sharpen, and Binarize.
// ============================================================================

using SkiaSharp;

namespace ImageTextify.Platform.Imaging.Preprocess;

/// <summary>
/// One stage of the preprocessing pipeline.
/// </summary>
/// <remarks>
/// Each stage is a pure function: it takes a source bitmap, returns a new
/// bitmap, and never mutates the source. The orchestrator disposes the
/// intermediate bitmaps as the pipeline advances.
/// </remarks>
internal interface IImageStage
{
    /// <summary>
    /// The stage name, used in diagnostics and test failure messages.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Applies the stage to the source bitmap and returns a new bitmap.
    /// </summary>
    /// <param name="source">
    /// The bitmap to transform. The stage must not mutate it.
    /// </param>
    /// <param name="options">
    /// The pipeline options. Not null.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the stage. The stage honors the token.
    /// </param>
    /// <returns>
    /// A new bitmap. The caller owns it and disposes it. The caller also
    /// remains the owner of <paramref name="source"/> and disposes that
    /// separately.
    /// </returns>
    Task<SKBitmap> ApplyAsync(
        SKBitmap source,
        ImagePreprocessingOptions options,
        CancellationToken cancellationToken);
}