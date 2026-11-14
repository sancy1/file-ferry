// ============================================================================
// FILE:     src/ImageTextify.Platform/Imaging/Preprocess/Deskew.cs
// PURPOSE:  Detects and corrects small skew angles. Uses a projection-variance
//           search across a bounded angle range. When the detected angle is
//           below the minimum correction, the source is returned unchanged.
// RELATION: Implements IImageStage. Second stage in the pipeline.
// ============================================================================

using SkiaSharp;

namespace ImageTextify.Platform.Imaging.Preprocess;

/// <summary>
/// Corrects small skew angles.
/// </summary>
/// <remarks>
/// Searches candidate angles between -10 and +10 degrees in 0.5 degree
/// steps. For each candidate, scores the variance of the horizontal
/// projection of dark pixels. The angle with the highest variance is the
/// best alignment of text baselines. If the best score does not beat the
/// zero-angle score by a clear margin, no rotation is applied.
/// </remarks>
internal sealed class Deskew : IImageStage
{
    private const double MinimumAngleDegrees = 0.5;
    private const double MaximumAngleDegrees = 10.0;
    private const double AngleStepDegrees = 0.5;
    private const int ProjectionRowCount = 96;
    private const double MinimumImprovementRatio = 1.10;

    /// <inheritdoc/>
    public string Name => "deskew";

    /// <inheritdoc/>
    public Task<SKBitmap> ApplyAsync(
        SKBitmap source,
        ImagePreprocessingOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        cancellationToken.ThrowIfCancellationRequested();

        var zeroScore = ScoreProjection(source, 0);
        var bestAngle = 0.0;
        var bestScore = zeroScore;

        for (var angle = -MaximumAngleDegrees; angle <= MaximumAngleDegrees; angle += AngleStepDegrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Math.Abs(angle) < MinimumAngleDegrees) continue;

            var score = ScoreProjection(source, angle);
            if (score > bestScore * MinimumImprovementRatio)
            {
                bestScore = score;
                bestAngle = angle;
            }
        }

        if (Math.Abs(bestAngle) < MinimumAngleDegrees)
        {
            return Task.FromResult(CopyOf(source));
        }

        return Task.FromResult(Rotate(source, bestAngle));
    }

    private static double ScoreProjection(SKBitmap bitmap, double angleDegrees)
    {
        var rows = new long[ProjectionRowCount];
        var stepX = Math.Max(1, bitmap.Width / 256);
        var rad = angleDegrees * Math.PI / 180.0;
        var tan = Math.Tan(rad);
        var cx = bitmap.Width / 2.0;

        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                var shearedY = (int)(y + tan * (x - cx));
                if (shearedY < 0 || shearedY >= bitmap.Height) continue;
                var c = bitmap.GetPixel(x, shearedY);
                var luminance = (c.Red + c.Green + c.Blue) / 3;
                if (luminance < 128)
                {
                    var bucket = Math.Min(ProjectionRowCount - 1, shearedY * ProjectionRowCount / bitmap.Height);
                    rows[bucket]++;
                }
            }
        }

        double mean = 0;
        foreach (var r in rows) mean += r;
        mean /= rows.Length;

        double variance = 0;
        foreach (var r in rows)
        {
            var d = r - mean;
            variance += d * d;
        }
        variance /= rows.Length;

        return variance;
    }

    private static SKBitmap Rotate(SKBitmap source, double degrees)
    {
        var rotated = new SKBitmap(source.Width, source.Height, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(rotated);
        canvas.Clear(SKColors.White);
        canvas.Translate(source.Width / 2f, source.Height / 2f);
        canvas.RotateDegrees((float)degrees);
        canvas.Translate(-source.Width / 2f, -source.Height / 2f);
        canvas.DrawBitmap(source, 0, 0);
        return rotated;
    }

    private static SKBitmap CopyOf(SKBitmap source)
    {
        var copy = new SKBitmap(source.Width, source.Height, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(copy);
        canvas.DrawBitmap(source, 0, 0);
        return copy;
    }
}