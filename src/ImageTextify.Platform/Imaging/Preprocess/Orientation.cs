// ============================================================================
// FILE:     src/ImageTextify.Platform/Imaging/Preprocess/Orientation.cs
// PURPOSE:  Detects whether the image is rotated 0, 90, 180, or 270 degrees
//           and rotates it to canonical orientation. Detection is heuristic
//           and conservative: when in doubt, the source is returned unchanged.
// RELATION: Implements IImageStage. First stage in the pipeline.
// ============================================================================

using SkiaSharp;

namespace ImageTextify.Platform.Imaging.Preprocess;

/// <summary>
/// Corrects a quarter-turn rotation.
/// </summary>
/// <remarks>
/// Detection compares the horizontal-projection variance of the four
/// candidate rotations. A correctly oriented page has strong horizontal
/// text-line structure; a rotated page does not. The heuristic is
/// conservative: if the best candidate does not beat the current
/// orientation by a clear margin, no rotation is applied.
/// </remarks>
internal sealed class Orientation : IImageStage
{
    private const double MinimumImprovementRatio = 2.0;
    private const int ProjectionRowCount = 64;

    /// <inheritdoc/>
    public string Name => "orientation";

    /// <inheritdoc/>
    public Task<SKBitmap> ApplyAsync(
        SKBitmap source,
        ImagePreprocessingOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        cancellationToken.ThrowIfCancellationRequested();

        var sourceScore = ScoreProjection(source);
        // 180 is intentionally excluded: horizontal projection variance
        // is invariant under 180-degree rotation, so this heuristic cannot
        // distinguish 0 from 180. Only 90 and 270 are distinguishable.
        var rotations = new[] { 90, 270 };
        var bestAngle = 0;
        var bestScore = sourceScore;

        foreach (var angle in rotations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var candidate = Rotate(source, angle);
            var score = ScoreProjection(candidate);
            if (score > bestScore * MinimumImprovementRatio)
            {
                bestScore = score;
                bestAngle = angle;
            }
        }

        if (bestAngle == 0)
        {
            return Task.FromResult(CopyOf(source));
        }

        return Task.FromResult(Rotate(source, bestAngle));
    }

    private static double ScoreProjection(SKBitmap bitmap)
    {
        var rows = new long[ProjectionRowCount];
        var stepX = Math.Max(1, bitmap.Width / 256);
        var stepY = Math.Max(1, bitmap.Height / ProjectionRowCount);

        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            var bucket = Math.Min(ProjectionRowCount - 1, y * ProjectionRowCount / bitmap.Height);
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                var c = bitmap.GetPixel(x, y);
                var luminance = (c.Red + c.Green + c.Blue) / 3;
                if (luminance < 128)
                {
                    rows[bucket]++;
                }
            }
        }

        if (rows.Length == 0) return 0;

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

    private static SKBitmap Rotate(SKBitmap source, int degrees)
    {
        var rotated = new SKBitmap(
            degrees == 180 ? source.Width : source.Height,
            degrees == 180 ? source.Height : source.Width,
            source.ColorType,
            source.AlphaType);

        using var canvas = new SKCanvas(rotated);
        canvas.Clear(SKColors.White);

        var cx = rotated.Width / 2f;
        var cy = rotated.Height / 2f;
        canvas.Translate(cx, cy);
        canvas.RotateDegrees(degrees);
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
