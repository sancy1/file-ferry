// filepath: src/FileHarvester.Tests/ImageTextify/OnnxOcrEnginePrintedFallbackTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 2.4e
// purpose:  Unit tests for the OnnxOcrEngine printed-text fallback
//           branch. Verifies gating on (enabled AND below-threshold)
//           and that accepted fallback uses QwenPrinted provenance.
//
// RELATIONSHIPS
//   Implements : n/a (test file)
//   Depends on : OnnxOcrEngine, FakeOcrModelRunner, FakeQwenVisionService,
//                FakeImageTextifySettingsStore
//   Used by    : dotnet test --filter OnnxOcrEnginePrintedFallbackTests
//   See also   : OnnxOcrEngine.cs, QwenPrintedFallbackRecognizerTests.cs
//
// NOTES
//   - Three tests cover: high-confidence lines never invoke the
//     fallback; only low-confidence lines invoke it; accepted fallback
//     uses QwenPrinted provenance and Qwen text.
//   - Uses FakeOcrModelRunner per-call PpocrRecognition overrides to
//     set per-box confidence. No real ONNX inference.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;
using ImageTextify.Core.Settings;
using ImageTextify.Platform.Imaging;
using SkiaSharp;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Unit tests for the OnnxOcrEngine printed-text fallback branch.
/// </summary>
public sealed class OnnxOcrEnginePrintedFallbackTests
{
    private static readonly byte[] TinyImage = CreateTinyPng();

    private static byte[] CreateTinyPng()
    {
        using var bmp = new SkiaSharp.SKBitmap(128, 64);
        using var data = bmp.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static List<DetectionBox> TwoBoxes() => new List<DetectionBox>
    {
        new DetectionBox(new OcrBoundingBox(0, 0, 100, 30), 0.9),
        new DetectionBox(new OcrBoundingBox(0, 40, 100, 30), 0.9),
    };

    private static List<DetectionBox> OneBox() => new List<DetectionBox>
    {
        new DetectionBox(new OcrBoundingBox(0, 0, 100, 30), 0.9),
    };

    private static async Task<OcrResult> RunAsync(
        FakeOcrModelRunner runner,
        FakeQwenVisionService qwen,
        bool enableFallback)
    {
        var settings = new InMemoryImageTextifySettingsStore(
            new ImageTextifySettings(enableFallback, 0.3));
        var printedFallback = new QwenPrintedFallbackRecognizer(qwen);
        var engine = new OnnxOcrEngine(
            runner,
            new ImagePreprocessor(),
            new OcrLineRouter(),
            new OcrResultMerger(),
            qwen: null,
            printedFallback: printedFallback,
            settings: settings);
        await using var stream = new MemoryStream(TinyImage);
        return await engine.ExtractAsync(stream, new ImageOcrOptions(), CancellationToken.None);
    }

    /// <summary>
    /// When the setting is ON but PP-OCR confidence is above the
    /// threshold, the fallback must not be invoked.
    /// </summary>
    [Fact]
    public async Task HighConfidenceLinesDoNotInvokeFallbackWhenEnabled()
    {
        var runner = new FakeOcrModelRunner(TwoBoxes(), "text", 0.9, "");
        var qwen = new FakeQwenVisionService { OutputToReturn = "should not be used" };

        var result = await RunAsync(runner, qwen, enableFallback: true);

        Assert.Equal(0, qwen.CallCount);
        foreach (var line in result.Lines)
        {
            Assert.NotEqual(OcrEngineKind.QwenPrinted, line.Engine);
        }
    }

    /// <summary>
    /// Only lines whose PP-OCR confidence is below the threshold must
    /// invoke the fallback. High-confidence lines skip it.
    /// </summary>
    [Fact]
    public async Task FallbackCalledOnlyOnLowConfidenceLines()
    {
        var perCall = new Dictionary<int, PpocrRecognition>
        {
            { 0, new PpocrRecognition("low", 0.1) },
            { 1, new PpocrRecognition("high", 0.9) },
        };
        var runner = new FakeOcrModelRunner(
            TwoBoxes(),
            perCall,
            new Dictionary<int, string>(),
            new PpocrRecognition("default", 0.5),
            "");
        var qwen = new FakeQwenVisionService { OutputToReturn = "not longer" };

        var result = await RunAsync(runner, qwen, enableFallback: true);

        Assert.Equal(1, qwen.CallCount);
        // line 0 was low-conf; Qwen returned "not longer" (10 chars vs
        // ppocr "low" 3 chars) which is longer, so it is accepted.
        // We only assert the high-conf line is Ppocr here.
        OcrLineResult? highConfLine = null;
        foreach (var l in result.Lines)
        {
            if (l.Text == "high") { highConfLine = l; break; }
        }
        Assert.NotNull(highConfLine);
        Assert.Equal(OcrEngineKind.Ppocr, highConfLine!.Engine);
    }

    /// <summary>
    /// When the fallback accepts a line, that line is attributed to
    /// QwenPrinted and uses the Qwen text.
    /// </summary>
    [Fact]
    public async Task AcceptedFallbackUsesQwenTextAndProvenance()
    {
        var perCall = new Dictionary<int, PpocrRecognition>
        {
            { 0, new PpocrRecognition("a", 0.1) },
        };
        var runner = new FakeOcrModelRunner(
            OneBox(),
            perCall,
            new Dictionary<int, string>(),
            new PpocrRecognition("default", 0.5),
            "");
        var qwen = new FakeQwenVisionService { OutputToReturn = "longer qwen text" };

        var result = await RunAsync(runner, qwen, enableFallback: true);

        Assert.Equal(1, qwen.CallCount);
        Assert.Single(result.Lines);
        var line = result.Lines[0];
        Assert.Equal(OcrEngineKind.QwenPrinted, line.Engine);
        Assert.Equal("longer qwen text", line.Text);
    }
}
