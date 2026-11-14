// ============================================================================
// FILE:     src/ImageTextify.Platform/Imaging/Preprocess/Contrast.cs
// PURPOSE:  Linear contrast stretch driven by the 2nd and 98th percentile of
//           the luminance histogram. Improves faint but present text. Preserves
//           color channels; does not force grayscale.
// RELATION: Implements IImageStage. Fourth stage in the pipeline.
// ============================================================================

using SkiaSharp;

namespace ImageTextify.Platform.Imaging.Preprocess;

/// <summary>
/// Linear contrast stretch from the 2nd to the 98th percentile.
/// </summary>
internal sealed class Contrast : IImageStage
{
    /// <inheritdoc/>
    public string Name => "contrast";

    /// <inheritdoc/>
    public Task<SKBitmap> ApplyAsync(
        SKBitmap source,
        ImagePreprocessingOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        cancellationToken.ThrowIfCancellationRequested();

        var histogram = new int[256];
        var stepX = Math.Max(1, source.Width / 512);
        var stepY = Math.Max(1, source.Height / 512);

        for (var y = 0; y < source.Height; y += stepY)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Width; x += stepX)
            {
                var c = source.GetPixel(x, y);
                var luminance = (c.Red + c.Green + c.Blue) / 3;
                histogram[luminance]++;
            }
        }

        long total = 0;
        foreach (var h in histogram) total += h;

        var lowTarget = total * 2 / 100;
        var highTarget = total * 98 / 100;

        long running = 0;
        int low = 0;
        int high = 255;

        for (var i = 0; i < 256; i++)
        {
            running += histogram[i];
            if (running >= lowTarget) { low = i; break; }
        }

        running = 0;
        for (var i = 0; i < 256; i++)
        {
            running += histogram[i];
            if (running >= highTarget) { high = i; break; }
        }

        if (high - low < 32)
        {
            return Task.FromResult(CopyOf(source));
        }

        var scale = 255.0 / (high - low);
        var output = new SKBitmap(source.Width, source.Height, source.ColorType, source.AlphaType);

        for (var y = 0; y < source.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var x = 0; x < source.Width; x++)
            {
                var c = source.GetPixel(x, y);
                var r = Stretch(c.Red, low, high, scale);
                var g = Stretch(c.Green, low, high, scale);
                var b = Stretch(c.Blue, low, high, scale);
                output.SetPixel(x, y, new SKColor(r, g, b, c.Alpha));
            }
        }

        return Task.FromResult(output);
    }

    private static byte Stretch(byte value, int low, int high, double scale)
    {
        var v = (value - low) * scale;
        if (v < 0) v = 0;
        if (v > 255) v = 255;
        return (byte)v;
    }

    private static SKBitmap CopyOf(SKBitmap source)
    {
        var copy = new SKBitmap(source.Width, source.Height, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(copy);
        canvas.DrawBitmap(source, 0, 0);
        return copy;
    }
}