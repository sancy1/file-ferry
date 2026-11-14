// ============================================================================
// FILE:     src/ImageTextify.Platform/Imaging/ImagePreprocessingDiagnostics.cs
// PURPOSE:  A structured record of what the preprocessing pipeline did.
//           Emitted for the log drawer and for tests. Never contains image
//           content or user text.
// RELATION: Returned in ImagePreprocessingResult.
// ============================================================================

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// A summary of the preprocessing pipeline's actions on one image.
/// </summary>
/// <param name="OrientationApplied">
/// True when the orientation stage rotated the image.
/// </param>
/// <param name="DeskewAngleDegrees">
/// The skew angle corrected, or null when no deskew was applied.
/// </param>
/// <param name="DenoiseApplied">
/// True when denoise ran.
/// </param>
/// <param name="ContrastApplied">
/// True when contrast normalization ran.
/// </param>
/// <param name="EffectiveDpi">
/// The effective DPI the pipeline used, either from the caller hint or
/// derived from the dimensions. Not a measured value.
/// </param>
/// <param name="UpscaleOutcome">
/// One of UpscaleSkipped, UpscaleApplied, or UpscaleCapReached.
/// </param>
/// <param name="UpscaleFactor">
/// The scale factor applied, or 1.0 when no upscale ran.
/// </param>
/// <param name="SharpenApplied">
/// True when sharpening ran.
/// </param>
/// <param name="BinarizeApplied">
/// True when Sauvola binarization ran.
/// </param>
public sealed record ImagePreprocessingDiagnostics(
    bool OrientationApplied,
    double? DeskewAngleDegrees,
    bool DenoiseApplied,
    bool ContrastApplied,
    int EffectiveDpi,
    string UpscaleOutcome,
    double UpscaleFactor,
    bool SharpenApplied,
    bool BinarizeApplied);