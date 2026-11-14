// ============================================================================
// FILE:     src/ImageTextify.Platform/Imaging/Preprocess/Upscale.cs
// PURPOSE:  Upscales the image when the effective DPI is below the configured
//           threshold. The effective DPI is derived from the caller hint or
//           from the dimensions; it is never read from file metadata. The
//           scale factor is capped so a very small image does not blow up
//           memory or take minutes.
// RELATION: Implements IImageStage. Sixth stage in the pipeline.
// ============================================================================

using SkiaSharp;

namespace ImageTextify.Platform.Imaging.Preprocess;

/// <summary>
/// Upscales low-resolution images toward an OCR-friendly effective DPI.
/// </summary>
/// <remarks>
/// Effective DPI is not a measured value. It is the caller hint when one is
/// supplied; otherwise 200 DPI for images wider than 1000 pixels, and 150 DPI
/// otherwise. Upscaling is attempted only when the effective DPI is below
/// the threshold. The scale factor is capped at
/// <see cref="ImagePreprocessingOptions.MaxUpscaleFactor"/>. When the cap
/// cannot reach the threshold, the upscale is skipped.
/// </remarks>
internal sealed class Upscale : IImageStage
{
    private const int WideImageWidthPixels = 1000;
    private const int WideImageEffectiveDpi = 200;
    private const int NarrowImageEffectiveDpi = 150;

    /// <inheritdoc/>
    public string Name => "upscale";

    /// <inheritdoc/>
    public Task<SKBitmap> ApplyAsync(
        SKBitmap source,
        ImagePreprocessingOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        cancellationToken.ThrowIfCancellationRequested();

        var effectiveDpi = options.EffectiveDpiHint
            ?? (source.Width > WideImageWidthPixels ? WideImageEffectiveDpi : NarrowImageEffectiveDpi);

        if (effectiveDpi >= options.UpscaleThresholdDpi)
        {
            return Task.FromResult(CopyOf(source));
        }

        var requiredFactor = (double)options.UpscaleThresholdDpi / effectiveDpi;

        if (requiredFactor > options.MaxUpscaleFactor)
        {
            return Task.FromResult(CopyOf(source));
        }

        var targetWidth = (int)Math.Round(source.Width * requiredFactor);
        var targetHeight = (int)Math.Round(source.Height * requiredFactor);

        var scaled = new SKBitmap(targetWidth, targetHeight, source.ColorType, source.AlphaType);
        using (var canvas = new SKCanvas(scaled))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint
            {
                IsAntialias = true,
            };
            canvas.DrawBitmap(source, new SKRect(0, 0, targetWidth, targetHeight), paint);
        }

        return Task.FromResult(scaled);
    }

    private static SKBitmap CopyOf(SKBitmap source)
    {
        var copy = new SKBitmap(source.Width, source.Height, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(copy);
        canvas.DrawBitmap(source, 0, 0);
        return copy;
    }
}
