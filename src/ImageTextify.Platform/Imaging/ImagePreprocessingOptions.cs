// ============================================================================
// FILE:     src/ImageTextify.Platform/Imaging/ImagePreprocessingOptions.cs
// PURPOSE:  Options controlling the deterministic preprocessing pipeline.
//           Sharpening and binarization are off by default because they are
//           destructive to faint text. Upscaling is governed by an effective
//           DPI estimate, never by trusted file metadata.
// RELATION: Consumed by IImagePreprocessor.
// ============================================================================

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// Options controlling the preprocessing pipeline.
/// </summary>
/// <param name="Sharpen">
/// When true, apply unsharp masking after upscaling. Off by default.
/// Sharpening amplifies noise and can damage faint text.
/// </param>
/// <param name="Binarize">
/// When true, apply Sauvola adaptive thresholding as the final stage.
/// Off by default. Neural OCR reads grayscale or color better than a
/// thresholded image, and hard thresholding erases faint pen strokes.
/// Only Sauvola is permitted; a global fixed threshold is not supported.
/// </param>
/// <param name="UpscaleThresholdDpi">
/// The effective DPI below which upscaling is attempted. Default 300.
/// Accuracy gains plateau at 300 DPI; below 200 DPI accuracy drops.
/// </param>
/// <param name="MaxUpscaleFactor">
/// The maximum scale factor applied by the upscale stage. Default 3.
/// Prevents absurd upscaling of very small images.
/// </param>
/// <param name="EffectiveDpiHint">
/// An optional caller-supplied effective DPI. When null, the pipeline
/// derives an estimate from the image dimensions. See IImagePreprocessor
/// remarks for the derivation rule. Not trusted file metadata.
/// </param>
public sealed record ImagePreprocessingOptions(
    bool Sharpen = false,
    bool Binarize = false,
    int UpscaleThresholdDpi = 300,
    int MaxUpscaleFactor = 3,
    int? EffectiveDpiHint = null);