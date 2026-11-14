// filepath: src/FileHarvester.Tests/ImageTextify/PreprocessorBlurDiagnostic.cs
// layer:    Tests
// since:    1.2.0
// purpose:  Stage-by-stage sharpness diagnostic. Runs each preprocessor stage in isolation, saves PNGs, prints Laplacian variance, file size, mean pixel value. Identifies the blurring stage. Does not modify production code.

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Platform.Imaging;
using ImageTextify.Platform.Imaging.Preprocess;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FileHarvester.Tests.ImageTextify;

public sealed class PreprocessorBlurDiagnostic
{
    private readonly ITestOutputHelper _out;

    public PreprocessorBlurDiagnostic(ITestOutputHelper output)
    {
        _out = output;
    }

    [Fact]
    public async Task RunStageByStageDiagnostic()
    {
        var imagePath = @"C:\Users\HP\Desktop\exracts\Images\sharp\form_document_1.png";
        var outDir = @"C:\Users\HP\Desktop\file-ferry\bench\probe\stage_outputs";
        Directory.CreateDirectory(outDir);

        using var source = File.Exists(imagePath) ? SKBitmap.Decode(imagePath) : CreateSynthetic(754, 1000);
        if (source is null) { _out.WriteLine("Could not decode source."); return; }

        var options = new ImagePreprocessingOptions();
        var ct = CancellationToken.None;

        Print("source", source, outDir);

        using var afterOrientation = await new Orientation().ApplyAsync(source, options, ct);
        Print("orientation", afterOrientation, outDir);

        using var afterDeskew = await new Deskew().ApplyAsync(afterOrientation, options, ct);
        Print("deskew", afterDeskew, outDir);

        using var afterDenoise = await new Denoise().ApplyAsync(afterDeskew, options, ct);
        Print("denoise", afterDenoise, outDir);

        using var afterContrast = await new Contrast().ApplyAsync(afterDenoise, options, ct);
        Print("contrast", afterContrast, outDir);

        using var afterUpscale = await new Upscale().ApplyAsync(afterContrast, options, ct);
        Print("upscale", afterUpscale, outDir);

        using var afterSharpen = await new Sharpen().ApplyAsync(afterUpscale, options, ct);
        Print("sharpen", afterSharpen, outDir);

        using var afterBinarize = await new Binarize().ApplyAsync(afterSharpen, options, ct);
        Print("binarize", afterBinarize, outDir);
    }

    private void Print(string stageName, SKBitmap bmp, string outDir)
    {
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        var pngPath = Path.Combine(outDir, stageName + ".png");
        File.WriteAllBytes(pngPath, data.ToArray());
        var bytes = new FileInfo(pngPath).Length;
        var lap = LaplacianVariance(bmp);
        var mean = MeanPixel(bmp);
        _out.WriteLine("Stage: " + stageName);
        _out.WriteLine("  Dimensions: " + bmp.Width + "x" + bmp.Height);
        _out.WriteLine("  File size:  " + bytes + " bytes");
        _out.WriteLine("  Laplacian:  " + lap.ToString("F2", CultureInfo.InvariantCulture));
        _out.WriteLine("  Mean pixel: " + mean.ToString("F2", CultureInfo.InvariantCulture));
        _out.WriteLine("");
    }

    private static double LaplacianVariance(SKBitmap bmp)
    {
        int stepX = Math.Max(1, bmp.Width / 256);
        int stepY = Math.Max(1, bmp.Height / 256);
        double sum = 0, sumSq = 0;
        int n = 0;
        for (int y = stepY; y < bmp.Height - stepY; y += stepY)
        {
            for (int x = stepX; x < bmp.Width - stepX; x += stepX)
            {
                double c = Gray(bmp.GetPixel(x, y));
                double l = Gray(bmp.GetPixel(x - stepX, y));
                double r = Gray(bmp.GetPixel(x + stepX, y));
                double u = Gray(bmp.GetPixel(x, y - stepY));
                double d = Gray(bmp.GetPixel(x, y + stepY));
                double lap = -4 * c + l + r + u + d;
                sum += lap;
                sumSq += lap * lap;
                n++;
            }
        }
        if (n == 0) return 0;
        double mean = sum / n;
        return sumSq / n - mean * mean;
    }

    private static double MeanPixel(SKBitmap bmp)
    {
        int stepX = Math.Max(1, bmp.Width / 128);
        int stepY = Math.Max(1, bmp.Height / 128);
        double sum = 0;
        int n = 0;
        for (int y = 0; y < bmp.Height; y += stepY)
        {
            for (int x = 0; x < bmp.Width; x += stepX)
            {
                sum += Gray(bmp.GetPixel(x, y));
                n++;
            }
        }
        return n == 0 ? 0 : sum / n;
    }

    private static double Gray(SKColor c) => (c.Red + c.Green + c.Blue) / 3.0;

    private static SKBitmap CreateSynthetic(int w, int h)
    {
        var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Black };
        for (var y = 20; y < h - 20; y += 40) canvas.DrawRect(20, y, w - 40, 6, paint);
        return bmp;
    }
}
