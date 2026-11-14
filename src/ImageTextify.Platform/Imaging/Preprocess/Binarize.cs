// ============================================================================
// FILE:     src/ImageTextify.Platform/Imaging/Preprocess/Binarize.cs
// PURPOSE:  Applies Sauvola adaptive thresholding. The orchestrator skips
//           this stage unless options.Binarize is true. Off by default
//           because neural OCR reads grayscale or color better than a
//           thresholded image, and hard thresholding erases faint pen
//           strokes. A global fixed threshold is never used.
// RELATION: Implements IImageStage. Eighth stage, optional.
// ============================================================================

using SkiaSharp;

namespace ImageTextify.Platform.Imaging.Preprocess;

/// <summary>
/// Sauvola adaptive binarization.
/// </summary>
/// <remarks>
/// Computes a local threshold from the neighborhood mean and standard
/// deviation. Adaptive because uneven illumination is common in photographs
/// of documents. The output is a black-and-white SKBitmap.
/// </remarks>
internal sealed class Binarize : IImageStage
{
    private const int WindowRadius = 15;
    private const double K = 0.34;
    private const double DynamicRange = 128.0;

    /// <inheritdoc/>
    public string Name => "binarize";

    /// <inheritdoc/>
    public Task<SKBitmap> ApplyAsync(
        SKBitmap source,
        ImagePreprocessingOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        cancellationToken.ThrowIfCancellationRequested();

        var gray = new byte[source.Width * source.Height];
        for (var y = 0; y < source.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < source.Width; x++)
            {
                var c = source.GetPixel(x, y);
                gray[y * source.Width + x] = (byte)((c.Red + c.Green + c.Blue) / 3);
            }
        }

        var output = new SKBitmap(source.Width, source.Height, source.ColorType, source.AlphaType);

        for (var y = 0; y < source.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var x = 0; x < source.Width; x++)
            {
                double sum = 0;
                double sumSq = 0;
                var count = 0;

                var yMin = Math.Max(0, y - WindowRadius);
                var yMax = Math.Min(source.Height - 1, y + WindowRadius);
                var xMin = Math.Max(0, x - WindowRadius);
                var xMax = Math.Min(source.Width - 1, x + WindowRadius);

                for (var wy = yMin; wy <= yMax; wy++)
                {
                    for (var wx = xMin; wx <= xMax; wx++)
                    {
                        var v = gray[wy * source.Width + wx];
                        sum += v;
                        sumSq += v * v;
                        count++;
                    }
                }

                var mean = sum / count;
                var variance = (sumSq / count) - (mean * mean);
                var stdDev = variance > 0 ? Math.Sqrt(variance) : 0;
                var threshold = mean * (1.0 + K * ((stdDev / DynamicRange) - 1.0));

                var pixel = gray[y * source.Width + x];
                var value = pixel > threshold ? (byte)255 : (byte)0;
                output.SetPixel(x, y, new SKColor(value, value, value));
            }
        }

        return Task.FromResult(output);
    }
}