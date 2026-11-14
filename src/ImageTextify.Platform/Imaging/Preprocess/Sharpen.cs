// ============================================================================
// FILE:     src/ImageTextify.Platform/Imaging/Preprocess/Sharpen.cs
// PURPOSE:  Applies an unsharp mask. The orchestrator skips this stage unless
//           options.Sharpen is true. Off by default because sharpening
//           amplifies noise and can damage faint text.
// RELATION: Implements IImageStage. Seventh stage, optional.
// ============================================================================

using SkiaSharp;

namespace ImageTextify.Platform.Imaging.Preprocess;

/// <summary>
/// Applies an unsharp mask.
/// </summary>
/// <remarks>
/// Blends the source with a blurred copy at a conservative strength. The
/// strength and radius are fixed for Phase 2.3; there is no caller control
/// beyond turning the stage on or off.
/// </remarks>
internal sealed class Sharpen : IImageStage
{
    private const double Strength = 0.6;
    private const int Radius = 1;

    /// <inheritdoc/>
    public string Name => "sharpen";

    /// <inheritdoc/>
    public Task<SKBitmap> ApplyAsync(
        SKBitmap source,
        ImagePreprocessingOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        cancellationToken.ThrowIfCancellationRequested();

        var blurred = BoxBlur(source, Radius, cancellationToken);
        var output = new SKBitmap(source.Width, source.Height, source.ColorType, source.AlphaType);

        for (var y = 0; y < source.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var x = 0; x < source.Width; x++)
            {
                var src = source.GetPixel(x, y);
                var blur = blurred.GetPixel(x, y);

                var r = Clamp(src.Red + Strength * (src.Red - blur.Red));
                var g = Clamp(src.Green + Strength * (src.Green - blur.Green));
                var b = Clamp(src.Blue + Strength * (src.Blue - blur.Blue));

                output.SetPixel(x, y, new SKColor(r, g, b, src.Alpha));
            }
        }

        blurred.Dispose();
        return Task.FromResult(output);
    }

    private static SKBitmap BoxBlur(SKBitmap source, int radius, CancellationToken cancellationToken)
    {
        var blurred = new SKBitmap(source.Width, source.Height, source.ColorType, source.AlphaType);

        for (var y = 0; y < source.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var x = 0; x < source.Width; x++)
            {
                long r = 0, g = 0, b = 0;
                var count = 0;

                for (var dy = -radius; dy <= radius; dy++)
                {
                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        var sx = x + dx;
                        var sy = y + dy;
                        if (sx < 0 || sx >= source.Width || sy < 0 || sy >= source.Height) continue;
                        var c = source.GetPixel(sx, sy);
                        r += c.Red; g += c.Green; b += c.Blue;
                        count++;
                    }
                }

                blurred.SetPixel(x, y, new SKColor(
                    (byte)(r / count),
                    (byte)(g / count),
                    (byte)(b / count)));
            }
        }

        return blurred;
    }

    private static byte Clamp(double value)
    {
        if (value < 0) return 0;
        if (value > 255) return 255;
        return (byte)value;
    }
}