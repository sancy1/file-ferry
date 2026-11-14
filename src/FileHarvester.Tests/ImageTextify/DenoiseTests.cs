// ============================================================================
// FILE:     src/FileHarvester.Tests/ImageTextify/DenoiseTests.cs
// PURPOSE:  Pins the behavior of the Denoise stage's sharp-image gate. A clean
//           sharp scan must skip the median filter (the filter blurs small
//           text); a noisy scan must run it. This test exists because the
//           blur defect was fixed in Denoise.cs without a regression test,
//           and a fix without a test silently regresses.
// RELATION: Guards src/ImageTextify.Platform/Imaging/Preprocess/Denoise.cs.
//           Closes finding F-DEN-003.
// ============================================================================

using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ImageTextify.Platform.Imaging;
using ImageTextify.Platform.Imaging.Preprocess;
using SkiaSharp;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

public sealed class DenoiseTests
{
    private static ImagePreprocessingOptions DefaultOptions() => new ImagePreprocessingOptions();

    [Fact]
    public async Task DenoiseSkipsFilterOnSharpImage()
    {
        // ARRANGE: a clearly sharp image (alternating black/white stripes).
        // Laplacian variance is in the tens of thousands, well above the
        // Denoise gate's 10,000 threshold, so the filter must be skipped
        // and the output must be an exact copy of the source.
        using var source = BuildStripedBitmap(64, 64);
        var denoise = new Denoise();

        // ACT
        using var output = await denoise.ApplyAsync(source, DefaultOptions(), CancellationToken.None);

        // ASSERT: dimensions preserved; every pixel equals its source pixel.
        output.Width.Should().Be(source.Width);
        output.Height.Should().Be(source.Height);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                output.GetPixel(x, y).Should().Be(
                    source.GetPixel(x, y),
                    "a sharp image must skip the median filter; any change at ({0},{1}) means the gate fired", x, y);
            }
        }
    }

    [Fact]
    public async Task DenoiseAppliesMedianFilterOnNoisyImage()
    {
        // ARRANGE: a genuinely noisy image (low-amplitude pixel-level
        // jitter, no high-contrast spikes). Laplacian variance is far
        // below 10,000, so the gate must fire, and the median filter must
        // (a) emit grayscale pixels and (b) alter at least one pixel.
        using var source = BuildNoisyBitmap(64, 64);
        var denoise = new Denoise();

        // ACT
        using var output = await denoise.ApplyAsync(source, DefaultOptions(), CancellationToken.None);

        // ASSERT 1: output is grayscale (R == G == B per pixel), which only
        // the median path produces. A sharp-image copy path would preserve
        // the color channels of the source and fail this assertion.
        for (var y = 0; y < output.Height; y++)
        {
            for (var x = 0; x < output.Width; x++)
            {
                var c = output.GetPixel(x, y);
                c.Red.Should().Be(c.Green, "median path emits gray; R must equal G at ({0},{1})", x, y);
                c.Green.Should().Be(c.Blue, "median path emits gray; G must equal B at ({0},{1})", x, y);
            }
        }

        // ASSERT 2: at least one salt spike was removed. The source has
        // black spikes on a mid-gray field; the median filter replaces each
        // spike with a neighborhood median, so the output at that pixel is
        // strictly brighter than the spike. Counting changes proves the
        // filter actually ran on the image, not on a no-op copy.
        var changed = 0;
        for (var y = 1; y < source.Height - 1; y++)
        {
            for (var x = 1; x < source.Width - 1; x++)
            {
                if (source.GetPixel(x, y).Red != output.GetPixel(x, y).Red) changed++;
            }
        }
        changed.Should().BeGreaterThan(0, "the median filter must alter at least one spike pixel");
    }

    private static SKBitmap BuildStripedBitmap(int w, int h)
    {
        var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        using var black = new SKPaint { Color = SKColors.Black };
        for (var y = 0; y < h; y += 2) canvas.DrawRect(0, y, w, 1, black);
        return bmp;
    }

    private static SKBitmap BuildNoisyBitmap(int w, int h)
    {
        // A genuinely noisy image for the gate metric: low-amplitude,
        // dense pixel-level jitter with no high-contrast spikes. The
        // Laplacian responds to local contrast squared, so +/-1 gray-level
        // jitter keeps variance far below the gate's 10,000 threshold.
        // The colors are deliberately NOT gray so the test can prove the
        // median path ran (it emits SKColor(median,median,median)) rather
        // than the copy path (which preserves the source colors).
        var bmp = new SKBitmap(w, h);
        var rng = new System.Random(12345);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var r = (byte)(128 + rng.Next(-1, 2));
                var g = (byte)(128 + rng.Next(-1, 2));
                var b = (byte)(128 + rng.Next(-1, 2));
                bmp.SetPixel(x, y, new SKColor(r, g, b));
            }
        }
        return bmp;
    }
}
