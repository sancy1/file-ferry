// filepath: src/ImageTextify.Platform/Imaging/OnnxOcrEngine.cs
// layer:    Boundary
// module:   ImageTextify.Platform.Imaging
// since:    Phase 2.4b2
// purpose:  Implementation of IImageOcrEngine that orchestrates the OCR
//           pipeline: preprocessing (via IImagePreprocessor), ONNX
//           inference (via IOcrModelRunner), per-line routing between
//           PP-OCR and TrOCR (via IOcrLineRouter), and reading-order
//           merging (via IOcrResultMerger).
//
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.IImageOcrEngine
//   Depends on : IOcrModelRunner, IImagePreprocessor, IOcrLineRouter,
//                IOcrResultMerger, SkiaBitmapHandle, SkiaSharp
//   Used by    : ImageTextify.App integration (Phase 2.6)
//   See also   : IOcrModelRunner.cs, OcrLineRouter.cs,
//                OcrResultMerger.cs, IImagePreprocessor.cs
//
// NOTES
//   - The engine owns the IOcrModelRunner passed in (disposes it on
//     DisposeAsync). The other three dependencies (preprocessor, router,
//     merger) are stateless or caller-owned; the engine does not dispose
//     them.
//   - Honors ImageOcrOptions.Enhance: when true (default), the pipeline
//     calls IImagePreprocessor.PreprocessAsync. When false, the raw
//     image is fed directly to detection.
//   - Cancellation propagates through every await. On cancellation the
//     engine disposes any partial bitmaps it created.
//   - TrOCR is a fallback, not a co-equal. Lines are only routed to
//     TrOCR when PP-OCR produces visible garbage per IOcrLineRouter.
//   - If PP-OCR detects zero boxes, the engine attempts TrOCR on the
//     whole image. This is the last-resort handwriting path.
//   - SkiaSharp SKBitmap is converted to RGBA bytes via a helper.
//     The conversion is O(pixels) with GetPixel; deferred optimization
//     to Block E (ImageTextify-072).

using System.Text;
using ImageTextify.Core.Abstractions;
using ImageTextify.Core.Settings;
using SkiaSharp;

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// Orchestrates the OCR pipeline: preprocess, detect, route, merge.
/// </summary>
public sealed class OnnxOcrEngine : IImageOcrEngine, IAsyncDisposable
{
    private readonly IOcrModelRunner _modelRunner;
    private readonly IImagePreprocessor _preprocessor;
    private readonly QwenHandwritingRecognizer? _qwenHandwriting;
    private readonly QwenPrintedFallbackRecognizer? _printedFallback;
    private readonly IImageTextifySettingsStore? _settings;
    private readonly IOcrLineRouter _router;
    private readonly IOcrResultMerger _merger;
    private bool _disposed;

    /// <summary>
    /// Creates the OCR engine from its dependencies.
    /// </summary>
    /// <param name="modelRunner">
    /// The ONNX inference runner. The engine takes ownership and disposes
    /// it on DisposeAsync. Not null.
    /// </param>
    /// <param name="preprocessor">
    /// The Phase 2.3 preprocessing pipeline. Not null. Not disposed by
    /// this engine.
    /// </param>
    /// <param name="router">
    /// The per-line routing decision. Not null. Not disposed by this
    /// engine.
    /// </param>
    /// <param name="merger">
    /// The per-line result merger. Not null. Not disposed by this engine.
    /// </param>
    /// <param name="qwen">
    /// Optional Qwen vision inference service for handwriting. When non-null,
    /// handwriting lines are recognized by Qwen instead of TrOCR. When null,
    /// the engine uses the existing TrOCR path (current behavior preserved).
    /// The <see cref="QwenHandwritingRecognizer"/> is created internally from
    /// this service.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any dependency is null.
    /// </exception>
    public OnnxOcrEngine(
        IOcrModelRunner modelRunner,
        IImagePreprocessor preprocessor,
        IOcrLineRouter router,
        IOcrResultMerger merger,
        IQwenVisionService? qwen = null)
    {
        _modelRunner = modelRunner ?? throw new ArgumentNullException(nameof(modelRunner));
        _preprocessor = preprocessor ?? throw new ArgumentNullException(nameof(preprocessor));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _merger = merger ?? throw new ArgumentNullException(nameof(merger));
        _qwenHandwriting = qwen is null
            ? null
            : new QwenHandwritingRecognizer(qwen);
    }


    /// <summary>
    /// Creates the OCR engine with optional printed-text fallback.
    /// The 5-arg constructor behavior is preserved; this overload adds
    /// a per-line Qwen printed-text fallback gated by user settings.
    /// </summary>
    /// <param name="modelRunner">
    /// The ONNX inference runner. Engine takes ownership; disposes on
    /// DisposeAsync. Not null.
    /// </param>
    /// <param name="preprocessor">
    /// The preprocessing pipeline. Not null. Not disposed by this engine.
    /// </param>
    /// <param name="router">
    /// The per-line routing decision. Not null. Not disposed by this engine.
    /// </param>
    /// <param name="merger">
    /// The per-line result merger. Not null. Not disposed by this engine.
    /// </param>
    /// <param name="qwen">
    /// Optional Qwen vision service for handwriting. When null, TrOCR
    /// path is used (5-arg behavior preserved). Not disposed by this engine.
    /// </param>
    /// <param name="printedFallback">
    /// Optional printed-text fallback. When null, the fallback branch
    /// is unreachable and behavior is identical to the 5-arg constructor.
    /// </param>
    /// <param name="settings">
    /// Optional settings store. When null, or when the store reports
    /// <see cref="IImageTextifySettings.EnableQwenPrintedFallback"/> false,
    /// the fallback branch is unreachable.
    /// </param>
    public OnnxOcrEngine(
        IOcrModelRunner modelRunner,
        IImagePreprocessor preprocessor,
        IOcrLineRouter router,
        IOcrResultMerger merger,
        IQwenVisionService? qwen,
        QwenPrintedFallbackRecognizer? printedFallback,
        IImageTextifySettingsStore? settings)
        : this(modelRunner, preprocessor, router, merger, qwen)
    {
        _printedFallback = printedFallback;
        _settings = settings;
    }
    /// <inheritdoc/>
    public async Task<OcrResult> ExtractAsync(
        Stream imageStream,
        ImageOcrOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(imageStream);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var warnings = new List<string>();

        // Snapshot the setting once per extraction so a mid-run
        // toggle cannot produce mixed results in one document.
        var settingsSnapshot = _settings?.Current;

        // Buffer the stream once. Required because SKBitmap.Decode consumes
        // the stream and the preprocessor also reads from the start.
        byte[] imageBytes;
        using (var ms = new MemoryStream())
        {
            await imageStream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            imageBytes = ms.ToArray();
        }

        if (imageBytes.Length == 0)
        {
            throw new InvalidOperationException("The image stream could not be decoded.");
        }

        // Preprocess or decode raw.
        SKBitmap sourceBitmap;
        ImagePreprocessingResult? preprocessResult = null;
        try
        {
            if (options.Enhance)
            {
                using var preprocessStream = new MemoryStream(imageBytes, writable: false);
                preprocessResult = await _preprocessor.PreprocessAsync(
                    preprocessStream,
                    new ImagePreprocessingOptions(),
                    cancellationToken).ConfigureAwait(false);
                sourceBitmap = preprocessResult.Enhanced.Bitmap;
            }
            else
            {
                using var decodeStream = new MemoryStream(imageBytes, writable: false);
                var decoded = SKBitmap.Decode(decodeStream);
                if (decoded is null)
                {
                    throw new InvalidOperationException(
                        "The image stream could not be decoded.");
                }
                sourceBitmap = decoded;
            }
        }
        catch
        {
            preprocessResult?.Dispose();
            throw;
        }

        try
        {
            var width = sourceBitmap.Width;
            var height = sourceBitmap.Height;

            // Run detection.
            var rgbaImage = SkiaRgbaExtractor.ToRgbaBytes(sourceBitmap);
            var boxes = await _modelRunner.RunDetectionAsync(
                rgbaImage, width, height, cancellationToken).ConfigureAwait(false);

            // If no boxes: whole-image TrOCR fallback.
            if (boxes.Count == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            string wholeText;
            if (_qwenHandwriting is not null && options.AttemptHandwritingRecognition)
            {
                // Qwen expects encoded image bytes (PNG/JPEG). The
                // whole image is raw RGBA; encode to PNG first.
                var wholePng = SkiaRgbaExtractor.EncodeToPng(sourceBitmap);
                wholeText = await _qwenHandwriting.RecognizeAsync(wholePng, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                wholeText = await _modelRunner.RunTrocrRecognitionAsync(
                    rgbaImage, width, height, cancellationToken).ConfigureAwait(false);
            }

                if (string.IsNullOrWhiteSpace(wholeText))
                {
                    warnings.Add("No text detected.");
                    return EmptyResult(warnings);
                }

                var wholeLine = new OcrLineResult(
                    Text: wholeText,
                    BoundingBox: new OcrBoundingBox(0, 0, width, height),
                    Confidence: null,
                    Engine: OcrEngineKind.TrocrWholeImage);

                return _merger.Merge(new[] { wholeLine }, warnings);
            }

            // Per-box: recognize, route, produce lines.
            var lines = new List<OcrLineResult>(boxes.Count);
            foreach (var box in boxes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var bbox = box.BoundingBox;
                if (bbox.Width < 4 || bbox.Height < 4) continue;

                var cropRgba = SkiaRgbaExtractor.ExtractRgbaCrop(
                    sourceBitmap, bbox.X, bbox.Y, bbox.Width, bbox.Height);

                var ppocr = await _modelRunner.RunPpocrRecognitionAsync(
                    cropRgba, bbox.Width, bbox.Height, cancellationToken)
                    .ConfigureAwait(false);

                // Guard: if PP-OCR itself produced near-zero confidence,
                // the crop is garbage (typically a false positive detection
                // on footer graphics, barcodes, or whitespace). Skip it
                // entirely. Routing to TrOCR would hallucinate content from
                // the multilingual dictionary over near-uniform logits.
                var ppocrConf = ppocr.Confidence ?? 0.0;
                if (ppocrConf < 0.1)
                {
                    continue;
                }

                var shouldRoute = _router.ShouldRouteToTrocr(
                    ppocr.Confidence, ppocr.Text, bbox.Width, bbox.Height);

                if (shouldRoute && options.AttemptHandwritingRecognition)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                string trocrText;
                if (_qwenHandwriting is not null)
                {
                    // Qwen expects encoded image bytes (PNG/JPEG). The
                    // crop is raw RGBA; encode to PNG first.
                    var cropPng = SkiaRgbaExtractor.ExtractPngCrop(
                        sourceBitmap, bbox.X, bbox.Y, bbox.Width, bbox.Height);
                    trocrText = await _qwenHandwriting.RecognizeAsync(cropPng, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    trocrText = await _modelRunner.RunTrocrRecognitionAsync(
                        cropRgba, bbox.Width, bbox.Height, cancellationToken)
                        .ConfigureAwait(false);
                }

                    if (!string.IsNullOrWhiteSpace(trocrText))
                    {
                        lines.Add(new OcrLineResult(
                            Text: trocrText,
                            BoundingBox: bbox,
                            Confidence: null,
                            Engine: OcrEngineKind.Trocr));
                        continue;
                    }
                    // TrOCR returned nothing; fall through to PP-OCR.
                }

                // Low-confidence lines are filtered by the guard above.
                // This branch handles only lines with PP-OCR confidence >= 0.1.
                // B.9 printed fallback. Only for printed lines (the
                // shouldRoute branch above already continued for
                // handwriting), only when the user opted in, and
                // only when PP-OCR confidence is below the threshold.
                // WHY before the PP-OCR add: if the fallback accepts, we
                // emit a QwenPrinted line and skip the PP-OCR line.
                // If it rejects, we fall through to the PP-OCR add.
                if (_printedFallback is not null
                    && settingsSnapshot is not null
                    && settingsSnapshot.EnableQwenPrintedFallback
                    && ppocrConf < settingsSnapshot.LowConfidenceThreshold)
                {
                    // Qwen expects encoded image bytes (PNG/JPEG). The
                    // crop is raw RGBA; encode to PNG first.
                    var printedCropPng = SkiaRgbaExtractor.ExtractPngCrop(
                        sourceBitmap, bbox.X, bbox.Y, bbox.Width, bbox.Height);
                    var qwenPrintedText = await _printedFallback.RecognizeAsync(
                        printedCropPng, ppocr.Text, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(qwenPrintedText))
                    {
                        lines.Add(new OcrLineResult(
                            Text: qwenPrintedText,
                            BoundingBox: bbox,
                            Confidence: null,
                            Engine: OcrEngineKind.QwenPrinted));
                        continue;
                    }
                }
                if (!string.IsNullOrWhiteSpace(ppocr.Text))
                {
                    lines.Add(new OcrLineResult(
                        Text: ppocr.Text,
                        BoundingBox: bbox,
                        Confidence: ppocr.Confidence,
                        Engine: OcrEngineKind.Ppocr));
                }
            }

            if (lines.Count == 0)
            {
                warnings.Add("No text detected.");
            }

            return _merger.Merge(lines, warnings);
        }
        finally
        {
            if (preprocessResult is not null)
            {
                preprocessResult.Dispose();
            }
            else
            {
                sourceBitmap.Dispose();
            }
        }
    }

    private static OcrResult EmptyResult(IReadOnlyList<string> warnings) =>
        new(
            Text: string.Empty,
            Confidence: null,
            Warnings: warnings,
            DetectedTextKind: OcrTextKind.Unknown,
            Lines: Array.Empty<OcrLineResult>());

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        return _modelRunner.DisposeAsync();
    }
}

/// <summary>
/// Converts SkiaSharp bitmaps to RGBA byte arrays for ONNX input.
/// </summary>
/// <remarks>
/// SKBitmap stores pixels in its own ColorType (typically Bgra8888 or
/// Rgba8888). This helper produces a canonical RGBA layout that the
/// ONNX model runner expects.
///
/// Performance: O(pixels) with SKBitmap.GetPixel. Adequate for the
/// current eval set. Deferred optimization to Block E
/// (ImageTextify-072).
/// </remarks>
internal static class SkiaRgbaExtractor
{
    /// <summary>
    /// Converts a full bitmap to RGBA bytes.
    /// </summary>
    /// <param name="bitmap">The source bitmap. Not null.</param>
    /// <returns>
    /// RGBA bytes in row-major order. Length = width * height * 4.
    /// </returns>
    public static byte[] ToRgbaBytes(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var w = bitmap.Width;
        var h = bitmap.Height;
        var bytes = new byte[w * h * 4];
        var i = 0;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var c = bitmap.GetPixel(x, y);
                bytes[i++] = c.Red;
                bytes[i++] = c.Green;
                bytes[i++] = c.Blue;
                bytes[i++] = c.Alpha;
            }
        }
        return bytes;
    }

    /// <summary>
    /// Extracts a crop from a bitmap and converts it to RGBA bytes.
    /// </summary>
    /// <param name="bitmap">The source bitmap. Not null.</param>
    /// <param name="x">Crop left edge in pixels. Must be within [0, bitmap.Width).</param>
    /// <param name="y">Crop top edge in pixels. Must be within [0, bitmap.Height).</param>
    /// <param name="w">Crop width. Must be positive and fit within the bitmap.</param>
    /// <param name="h">Crop height. Must be positive and fit within the bitmap.</param>
    /// <returns>
    /// RGBA bytes in row-major order. Length = w * h * 4.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the crop bounds fall outside the bitmap.
    /// </exception>
    public static byte[] ExtractRgbaCrop(SKBitmap bitmap, int x, int y, int w, int h)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (x < 0 || y < 0 || w <= 0 || h <= 0
            || x + w > bitmap.Width || y + h > bitmap.Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"Crop ({x},{y},{w},{h}) is outside bitmap {bitmap.Width}x{bitmap.Height}.");
        }

        var bytes = new byte[w * h * 4];
        var i = 0;
        for (var cy = 0; cy < h; cy++)
        {
            for (var cx = 0; cx < w; cx++)
            {
                var c = bitmap.GetPixel(x + cx, y + cy);
                bytes[i++] = c.Red;
                bytes[i++] = c.Green;
                bytes[i++] = c.Blue;
                bytes[i++] = c.Alpha;
            }
        }
        return bytes;
    }

    /// <summary>
    /// Encodes a full bitmap to PNG bytes for the Qwen vision pipeline,
    /// which requires a decodable image format (PNG/JPEG), not raw RGBA.
    /// </summary>
    /// <param name="bitmap">The source bitmap. Not null.</param>
    /// <returns>PNG-encoded bytes.</returns>
    public static byte[] EncodeToPng(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Extracts a crop from a bitmap and encodes it to PNG bytes for the
    /// Qwen vision pipeline, which requires a decodable image format
    /// (PNG/JPEG), not raw RGBA.
    /// </summary>
    /// <param name="bitmap">The source bitmap. Not null.</param>
    /// <param name="x">Crop left edge in pixels.</param>
    /// <param name="y">Crop top edge in pixels.</param>
    /// <param name="w">Crop width. Must be positive.</param>
    /// <param name="h">Crop height. Must be positive.</param>
    /// <returns>PNG-encoded bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the crop bounds fall outside the bitmap.
    /// </exception>
    public static byte[] ExtractPngCrop(SKBitmap bitmap, int x, int y, int w, int h)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (x < 0 || y < 0 || w <= 0 || h <= 0
            || x + w > bitmap.Width || y + h > bitmap.Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"Crop ({x},{y},{w},{h}) is outside bitmap {bitmap.Width}x{bitmap.Height}.");
        }
        using var crop = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(crop))
        {
            canvas.DrawBitmap(bitmap, new SKRect(x, y, x + w, y + h), new SKRect(0, 0, w, h));
            canvas.Flush();
        }
        using var data = crop.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
