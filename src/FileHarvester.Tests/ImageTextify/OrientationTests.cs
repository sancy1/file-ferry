// filepath: src/FileHarvester.Tests/ImageTextify/OrientationTests.cs
// layer:    Tests
// since:    1.2.0
// purpose:  Regression tests for Orientation stage. Verifies a correctly-oriented image is not rotated. Guards the 180-degree false positive.

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Platform.Imaging;
using ImageTextify.Platform.Imaging.Preprocess;
using SkiaSharp;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

public sealed class OrientationTests
{
    [Fact]
    public async Task OrientationDoesNotRotateSyntheticHorizontalImage()
    {
        using var input = CreateSyntheticHorizontal(754, 1000);
        var inputW = input.Width;
        var inputH = input.Height;
        var sut = new Orientation();
        using var output = await sut.ApplyAsync(input, new ImagePreprocessingOptions(), CancellationToken.None);
        Assert.Equal(inputW, output.Width);
        Assert.Equal(inputH, output.Height);
        var similarity = PixelSimilarity(input, output);
        var simStr = similarity.ToString("F4", CultureInfo.InvariantCulture);
        Assert.True(similarity > 0.95, "Orientation changed a correctly-oriented synthetic image. Similarity=" + simStr);
    }

    [Fact]
    public async Task OrientationDoesNotRotateFormDocumentOne()
    {
        var path = @"C:\Users\HP\Desktop\exracts\Images\sharp\form_document_1.png";
        if (!File.Exists(path)) return;
        using var input = SKBitmap.Decode(path);
        if (input is null) return;
        var inputW = input.Width;
        var inputH = input.Height;
        var sut = new Orientation();
        using var output = await sut.ApplyAsync(input, new ImagePreprocessingOptions(), CancellationToken.None);
        Assert.Equal(inputW, output.Width);
        Assert.Equal(inputH, output.Height);
        var similarity = PixelSimilarity(input, output);
        var simStr = similarity.ToString("F4", CultureInfo.InvariantCulture);
        Assert.True(similarity > 0.95, "Orientation rotated form_document_1.png. Similarity=" + simStr);
    }

    private static SKBitmap CreateSyntheticHorizontal(int w, int h)
    {
        var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Black };
        for (var y = 20; y < h - 20; y += 40)
        {
            canvas.DrawRect(20, y, w - 40, 6, paint);
        }
        return bmp;
    }

    private static double PixelSimilarity(SKBitmap a, SKBitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return 0.0;
        int same = 0;
        int total = 0;
        var stepX = Math.Max(1, a.Width / 64);
        var stepY = Math.Max(1, a.Height / 64);
        for (var y = 0; y < a.Height; y += stepY)
        {
            for (var x = 0; x < a.Width; x += stepX)
            {
                var ca = a.GetPixel(x, y);
                var cb = b.GetPixel(x, y);
                var d = Math.Abs(ca.Red - cb.Red) + Math.Abs(ca.Green - cb.Green) + Math.Abs(ca.Blue - cb.Blue);
                if (d < 30) same++;
                total++;
            }
        }
        return (double)same / total;
    }
}
