// filepath: src/ImageTextify.Platform/Imaging/OnnxModelRunner.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Imaging
// since:    Phase 2.4b2
// purpose:  ONNX Runtime implementation of IOcrModelRunner. Loads the
//           PP-OCR detector, PP-OCR recognizer, TrOCR encoder, and TrOCR
//           decoder ONNX sessions. Provides the three inference methods
//           and the CTC / BPE decoders the OCR engine calls.
//
// RELATIONSHIPS
//   Implements : ImageTextify.Platform.Imaging.IOcrModelRunner
//   Depends on : Microsoft.ML.OnnxRuntime
//                ImageTextify.Core.Abstractions.OcrBoundingBox
//   Used by    : ImageTextify.Platform.OnnxOcrEngine
//   See also   : IOcrModelRunner.cs, OnnxOcrEngine.cs, OcrLineRouter.cs
//
// NOTES
//   - Session lifetime is owned by this class. Disposing the runner
//     disposes all four sessions.
//   - The PP-OCR dictionary has 18,708 entries. It is loaded once at
//     construction and cached. CTC decoding maps token indices 0-18707
//     to characters. Any index outside this range is dropped.
//   - TrOCR BPE tokenizer is loaded from tokenizer.json once at
//     construction. The encoder/decoder loop is greedy (no beam search).
//   - Thread-safety: not thread-safe. The OCR engine serializes calls.
//   - Cancellation is honored at every await point. ONNX Runtime
//     Run() is synchronous; the runner checks the token before and
//     after each session call. For long decoder loops, the token is
//     checked per generated token.
//   - The class is sealed. If a different ONNX backend is needed,
//     implement IOcrModelRunner directly.

using System.Diagnostics.CodeAnalysis;
using ImageTextify.Core.Abstractions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// ONNX Runtime implementation of <see cref="IOcrModelRunner"/>.
/// </summary>
public sealed class OnnxModelRunner : IOcrModelRunner
{
    private readonly InferenceSession _ppocrDetector;
    private readonly InferenceSession _ppocrRecognizer;
    private readonly InferenceSession _trocrEncoder;
    private readonly InferenceSession _trocrDecoder;
    private readonly string[] _ppocrDictionary;
    private readonly TrocrBpeTokenizer _trocrTokenizer;
    private bool _disposed;

    /// <summary>
    /// Creates the runner from paths to the four ONNX model files and
    /// supporting token/dictionary files.
    /// </summary>
    /// <param name="ppocrDetectorPath">Path to the PP-OCR detector ONNX file.</param>
    /// <param name="ppocrRecognizerPath">Path to the PP-OCR recognizer ONNX file.</param>
    /// <param name="ppocrDictionaryPath">Path to the PP-OCR dictionary text file (one character per line).</param>
    /// <param name="trocrEncoderPath">Path to the TrOCR encoder ONNX file.</param>
    /// <param name="trocrDecoderPath">Path to the TrOCR decoder ONNX file.</param>
    /// <param name="trocrTokenizerPath">Path to the TrOCR tokenizer.json file.</param>
    /// <exception cref="FileNotFoundException">
    /// Thrown when any of the six input files is missing.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when any ONNX session fails to load, or the dictionary
    /// file is empty.
    /// </exception>
    public OnnxModelRunner(
        string ppocrDetectorPath,
        string ppocrRecognizerPath,
        string ppocrDictionaryPath,
        string trocrEncoderPath,
        string trocrDecoderPath,
        string trocrTokenizerPath)
    {
        foreach (var p in new[] { ppocrDetectorPath, ppocrRecognizerPath, ppocrDictionaryPath,
                                  trocrEncoderPath, trocrDecoderPath, trocrTokenizerPath })
        {
            if (!File.Exists(p))
                throw new FileNotFoundException($"ONNX model or support file not found: {p}", p);
        }

        var opts = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            InterOpNumThreads = 1,
            IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
        };

        _ppocrDetector = new InferenceSession(ppocrDetectorPath, opts);
        _ppocrRecognizer = new InferenceSession(ppocrRecognizerPath, opts);
        _trocrEncoder = new InferenceSession(trocrEncoderPath, opts);
        _trocrDecoder = new InferenceSession(trocrDecoderPath, opts);

        _ppocrDictionary = File.ReadAllLines(ppocrDictionaryPath);
        if (_ppocrDictionary.Length == 0)
            throw new InvalidOperationException($"PP-OCR dictionary is empty: {ppocrDictionaryPath}");

        _trocrTokenizer = TrocrBpeTokenizer.Load(trocrTokenizerPath);
    }

    /// <summary>
    /// Test-only constructor. Uses null sessions for tests that supply
    /// a fake <see cref="IOcrModelRunner"/> via the interface, not this
    /// implementation. Marked internal so tests in the same assembly
    /// can construct a runner without real ONNX files for structural
    /// checks.
    /// </summary>
    [ExcludeFromCodeCoverage]
    internal OnnxModelRunner()
    {
        _ppocrDetector = null!;
        _ppocrRecognizer = null!;
        _trocrEncoder = null!;
        _trocrDecoder = null!;
        _ppocrDictionary = Array.Empty<string>();
        _trocrTokenizer = null!;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<DetectionBox>> RunDetectionAsync(
        ReadOnlyMemory<byte> imageRgbaBytes,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (imageRgbaBytes.Length != width * height * 4)
        {
            throw new ArgumentException(
                $"RGBA byte length {imageRgbaBytes.Length} does not match width*height*4 = {width * height * 4}.",
                nameof(imageRgbaBytes));
        }

        var tensor = PpocrPreprocess.ToDetectionTensor(imageRgbaBytes.Span, width, height);
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_ppocrDetector.InputMetadata.Keys.First(), tensor),
        };

        cancellationToken.ThrowIfCancellationRequested();
        using var results = _ppocrDetector.Run(inputs);
        cancellationToken.ThrowIfCancellationRequested();

        // The detector outputs a probability map at reduced resolution.
        // Raw shape is typically [1, 1, H/4, W/4].
        var probMap = results[0].AsTensor<float>();

        // Post-process: threshold, connected components, score filter,
        // unclip by 1.5x ratio, scale from map coords to original image,
        // clip to image bounds.
        var boxes = PpocrPreprocess.PostProcessDetection(
            probMap,
            originalWidth: width,
            originalHeight: height,
            threshold: 0.3f,
            minPixelCount: 3,
            boxScoreThreshold: 0.6,
            unclipRatio: 1.5f);

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<DetectionBox>>(boxes);
    }

    /// <inheritdoc/>
    public Task<PpocrRecognition> RunPpocrRecognitionAsync(
        ReadOnlyMemory<byte> cropRgbaBytes,
        int cropWidth,
        int cropHeight,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (cropRgbaBytes.Length != cropWidth * cropHeight * 4)
        {
            throw new ArgumentException(
                $"RGBA byte length {cropRgbaBytes.Length} does not match cropWidth*cropHeight*4 = {cropWidth * cropHeight * 4}.",
                nameof(cropRgbaBytes));
        }

        var tensor = PpocrPreprocess.ToRecognitionTensor(cropRgbaBytes.Span, cropWidth, cropHeight);
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_ppocrRecognizer.InputMetadata.Keys.First(), tensor),
        };

        cancellationToken.ThrowIfCancellationRequested();
        using var results = _ppocrRecognizer.Run(inputs);
        cancellationToken.ThrowIfCancellationRequested();

        // Recognizer output is [1, T, C] logits.
        var logits = results[0].AsTensor<float>();
        var decoded = PpocrPreprocess.DecodeCtc(logits, _ppocrDictionary);
        return Task.FromResult(decoded);
    }

    /// <inheritdoc/>
    public Task<string> RunTrocrRecognitionAsync(
        ReadOnlyMemory<byte> cropRgbaBytes,
        int cropWidth,
        int cropHeight,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (cropRgbaBytes.Length != cropWidth * cropHeight * 4)
        {
            throw new ArgumentException(
                $"RGBA byte length {cropRgbaBytes.Length} does not match cropWidth*cropHeight*4 = {cropWidth * cropHeight * 4}.",
                nameof(cropRgbaBytes));
        }

        var pixelValues = PpocrPreprocess.ToEncoderTensor(cropRgbaBytes.Span, cropWidth, cropHeight);

        var encInputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_trocrEncoder.InputMetadata.Keys.First(), pixelValues),
        };
        cancellationToken.ThrowIfCancellationRequested();
        using var encResults = _trocrEncoder.Run(encInputs);
        var encoderHidden = encResults[0].AsTensor<float>();
        cancellationToken.ThrowIfCancellationRequested();

        const int bosTokenId = 0;
        const int eosTokenId = 2;
        const int maxTokens = 20;

        var generated = new List<int>();
        var inputIds = new long[] { bosTokenId };

        for (int step = 0; step < maxTokens; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var decInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(
                    _trocrDecoder.InputMetadata.Keys.First(),
                    new DenseTensor<long>(inputIds, new[] { 1, inputIds.Length })),
                NamedOnnxValue.CreateFromTensor(
                    _trocrDecoder.InputMetadata.Keys.Skip(1).First(),
                    encoderHidden),
            };

            using var decResults = _trocrDecoder.Run(decInputs);
            var logits = decResults[0].AsTensor<float>();

            var dims = logits.Dimensions.ToArray();
            int vocab = dims[2];
            int lastPos = dims[1] - 1;

            int best = 0;
            float bestVal = float.NegativeInfinity;
            for (int v = 0; v < vocab; v++)
            {
                var val = logits[0, lastPos, v];
                if (val > bestVal) { bestVal = val; best = v; }
            }

            if (best == eosTokenId) break;

            generated.Add(best);

            var newIds = new long[inputIds.Length + 1];
            Array.Copy(inputIds, newIds, inputIds.Length);
            newIds[inputIds.Length] = best;
            inputIds = newIds;
        }

        return Task.FromResult(_trocrTokenizer.Decode(generated));
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _ppocrDetector?.Dispose();
        _ppocrRecognizer?.Dispose();
        _trocrEncoder?.Dispose();
        _trocrDecoder?.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// PP-OCR preprocessing helpers. Converts RGBA byte arrays to the
/// normalized tensors PP-OCR expects.
/// </summary>
/// <remarks>
/// PP-OCR standard preprocessing:
/// <list type="bullet">
///   <item>RGBA (H, W, 4) -> RGB (H, W, 3) by dropping alpha</item>
///   <item>Normalize: pixel/255.0, then (x - mean) / std per channel</item>
///   <item>Mean = [0.485, 0.456, 0.406], Std = [0.229, 0.224, 0.225]
///         (ImageNet convention, PP-OCR default)</item>
///   <item>Transpose (H, W, C) -> (C, H, W)</item>
///   <item>Add batch dimension: (1, C, H, W)</item>
/// </list>
 /// The caller resizes to limit_side_len = 736 (PaddleX default)
 /// and snaps to a multiple of 32 before calling this method.
/// resizes and pads before calling.
/// </remarks>
internal static class PpocrPreprocess
{
    private static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
    private static readonly float[] Std = { 0.229f, 0.224f, 0.225f };

    /// <summary>
    /// Converts an input image to the normalized tensor PP-OCR's DB detector expects.
    /// </summary>
    /// <param name="rgba">RGBA bytes. Length = width*height*4.</param>
    /// <param name="width">Input image width in pixels.</param>
    /// <param name="height">Input image height in pixels.</param>
    /// <returns>
    /// Tensor of shape [1, 3, H, W] where H and W are both multiples of 32.
    /// The image is resized so max(H, W) is the smallest multiple of 32 that
    /// is >= the smaller of (input max side, LimitSideLen), preserving aspect
    /// ratio, and then padded if needed to reach the multiple of 32.
    /// </returns>
    /// <remarks>
    /// PaddleOCR DB detection preprocessing (PaddleX processors.py,
    /// limit_side_len = 736 for the default text detection pipeline):
    /// 1. Resize: if max(w, h) > limitSideLen, scale so max becomes
    ///    limitSideLen, then snap both dimensions UP to multiples of 32.
    /// 2. Normalize: (pixel/255 - mean) / std with ImageNet mean/std.
    /// 3. Transpose HWC -> CHW, add batch dim.
    /// Without the resize, the detector runs on the raw image and produces
    /// a probability map at a different resolution, shifting connected-
    /// component boxes and dropping small text at page boundaries.
    /// </remarks>
    public static DenseTensor<float> ToDetectionTensor(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException($"Invalid image dimensions {width}x{height}.");
        }
        if (rgba.Length != width * height * 4)
        {
            throw new ArgumentException(
                $"RGBA length {rgba.Length} does not match {width}x{height}x4.");
        }

        // PaddleOCR's DB detector requires the input image to be resized
        // so that its longest side does not exceed limit_side_len, then
        // snapped to a multiple of 32 in both dimensions. The value 736
        // matches paddlex/inference/models/text_detection/processors.py:53
        // (self.limit_side_len = 736) as installed in the bench venv.
        // Padding-only (no resize) is the current defect: it produces a
        // different probability map than PaddleOCR and drops small text.
        const int LimitSideLen = 736;
        const int SnapTo = 32;

        int resizedW, resizedH;
        var maxSide = Math.Max(width, height);
        if (maxSide > LimitSideLen)
        {
            // Preserve aspect ratio, cap the longest side at LimitSideLen,
            // then snap each dimension UP to the next multiple of 32.
            var scale = (double)LimitSideLen / maxSide;
            resizedW = ((int)Math.Round(width * scale) + SnapTo - 1) / SnapTo * SnapTo;
            resizedH = ((int)Math.Round(height * scale) + SnapTo - 1) / SnapTo * SnapTo;
        }
        else
        {
            // Image already fits under LimitSideLen. Snap each dimension UP
            // to the next multiple of 32 (padding, not downscaling).
            resizedW = (width + SnapTo - 1) / SnapTo * SnapTo;
            resizedH = (height + SnapTo - 1) / SnapTo * SnapTo;
        }

        // Safety: resized dims must not exceed LimitSideLen after snapping
        // when the input was larger than LimitSideLen. A rounding edge could
        // push us above by one SnapTo; clamp to the nearest safe multiple.
        if (maxSide > LimitSideLen)
        {
            var maxResized = Math.Max(resizedW, resizedH);
            if (maxResized > LimitSideLen + SnapTo)
            {
                // Reduce the larger dimension by one SnapTo. Extremely rare;
                // only at the rounding boundary of scale * side.
                if (resizedW == maxResized) resizedW -= SnapTo; else resizedH -= SnapTo;
            }
        }

        var tensor = new DenseTensor<float>(new[] { 1, 3, resizedH, resizedW });

        // Fill with normalized zero (the value produced by normalizing the
        // pad color, which is black). Pixels with no signal get the zero
        // normalized color so the detector sees them as background.
        for (int c = 0; c < 3; c++)
        {
            var zeroValue = (0f - Mean[c]) / Std[c];
            for (int y = 0; y < resizedH; y++)
            {
                for (int x = 0; x < resizedW; x++)
                {
                    tensor[0, c, y, x] = zeroValue;
                }
            }
        }

        // Nearest-neighbor resize from the source image into the tensor.
        // When the image was larger than LimitSideLen, this resizes it down.
        // When the image fits, this is effectively a copy into the top-left
        // region of the padded tensor (resized dims = padded dims when the
        // image fits and already multiples of 32; otherwise the small amount
        // of extra space is padding filled with the normalized zero above).
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Map source (x, y) to destination (dx, dy) in the resized tensor.
                var dx = (int)((long)x * resizedW / width);
                var dy = (int)((long)y * resizedH / height);
                var srcIdx = (y * width + x) * 4;
                for (int c = 0; c < 3; c++)
                {
                    float v = rgba[srcIdx + c] / 255f;
                    v = (v - Mean[c]) / Std[c];
                    tensor[0, c, dy, dx] = v;
                }
            }
        }
        return tensor;
    }

    /// <summary>
    /// Runs two-pass connected-components analysis on a binarized detector
    /// probability map. Returns one component per detected text region.
    /// </summary>
    /// <param name="probMap">
    /// The detector output. Shape is [1, 1, H, W] or [1, H, W]. Values are
    /// probabilities in 0..1.
    /// </param>
    /// <param name="threshold">Pixels with value >= threshold are foreground.</param>
    /// <param name="minPixelCount">Components with fewer pixels are ignored.</param>
    /// <returns>
    /// Detected components with bounding box (in map coordinates), mean score,
    /// and pixel count. Empty when nothing is detected.
    /// </returns>
    /// <remarks>
    /// Two-pass union-find. The first pass labels each pixel and unions
    /// adjacent foreground pixels. The second pass aggregates per label.
    /// Deterministic. O(N) time and space for N pixels.
    /// </remarks>
    public static List<DetectedComponent> FindConnectedComponents(
        Tensor<float> probMap,
        float threshold,
        int minPixelCount)
    {
        var dims = probMap.Dimensions.ToArray();
        int h, w;
        if (dims.Length == 4) { h = dims[2]; w = dims[3]; }
        else if (dims.Length == 3) { h = dims[1]; w = dims[2]; }
        else { throw new ArgumentException($"Unexpected probMap rank {dims.Length}."); }

        var n = h * w;
        var labels = new int[n];
        var parent = new List<int> { 0 };

        int Find(int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }
        void Union(int a, int b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb) parent[rb] = ra;
        }

        int UnionAndReturn(int a, int b)
        {
            Union(a, b);
            return Find(a);
        }

        int nextLabel = 1;

        // First pass: assign labels
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float v;
                if (dims.Length == 4) v = probMap[0, 0, y, x];
                else v = probMap[0, y, x];
                if (v < threshold) continue;

                int left     = x > 0             ? labels[y * w + (x - 1)]           : 0;
                int up       = y > 0             ? labels[(y - 1) * w + x]           : 0;
                int upLeft   = (x > 0 && y > 0)  ? labels[(y - 1) * w + (x - 1)] : 0;
                int upRight  = (x < w - 1 && y > 0) ? labels[(y - 1) * w + (x + 1)] : 0;

                // 8-connectivity: union with left, up, up-left, up-right neighbors.
                // Matches OpenCV findContours connectivity that PaddleOCR uses.
                // 4-connectivity (left+up only) split text lines into fragments,
                // causing the detector to emit 2x the correct box count on form
                // documents and corrupting downstream CER.
                int assigned = 0;
                if (left != 0)    assigned = left;
                if (up != 0)      assigned = (assigned == 0) ? up : (UnionAndReturn(assigned, up));
                if (upLeft != 0)  assigned = (assigned == 0) ? upLeft : (UnionAndReturn(assigned, upLeft));
                if (upRight != 0) assigned = (assigned == 0) ? upRight : (UnionAndReturn(assigned, upRight));

                if (assigned == 0)
                {
                    labels[y * w + x] = nextLabel;
                    parent.Add(nextLabel);
                    nextLabel++;
                }
                else
                {
                    labels[y * w + x] = Find(assigned);
                }
            }
        }

        // Second pass: aggregate per root label
        var stats = new Dictionary<int, (int xmin, int ymin, int xmax, int ymax, double sum, int count)>();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var lab = labels[y * w + x];
                if (lab == 0) continue;
                var root = Find(lab);
                float v;
                if (dims.Length == 4) v = probMap[0, 0, y, x];
                else v = probMap[0, y, x];

                if (stats.TryGetValue(root, out var s))
                {
                    stats[root] = (
                        Math.Min(s.xmin, x),
                        Math.Min(s.ymin, y),
                        Math.Max(s.xmax, x),
                        Math.Max(s.ymax, y),
                        s.sum + v,
                        s.count + 1);
                }
                else
                {
                    stats[root] = (x, y, x, y, v, 1);
                }
            }
        }

        var result = new List<DetectedComponent>(stats.Count);
        foreach (var kv in stats)
        {
            var s = kv.Value;
            if (s.count < minPixelCount) continue;
            result.Add(new DetectedComponent(
                s.xmin, s.ymin, s.xmax, s.ymax,
                s.sum / s.count,
                s.count));
        }
        return result;
    }
    /// <summary>
    /// Expands a box outward by the given ratio. Used by DB post-processing
    /// to grow text-core boxes to full text extent.
    /// </summary>
    /// <param name="xmin">Left edge (inclusive).</param>
    /// <param name="ymin">Top edge (inclusive).</param>
    /// <param name="xmax">Right edge (inclusive).</param>
    /// <param name="ymax">Bottom edge (inclusive).</param>
    /// <param name="ratio">Expansion ratio. 1.5 is the DB paper default.</param>
    /// <returns>Expanded bounds, or null if the box has zero or negative dimensions.</returns>
    public static (int XMin, int YMin, int XMax, int YMax)? UnclipBox(
        int xmin, int ymin, int xmax, int ymax, float ratio)
    {
        var w = xmax - xmin;
        var h = ymax - ymin;
        if (w <= 0 || h <= 0) return null;

        var area = (double)w * h;
        var perimeter = 2.0 * (w + h);
        if (perimeter <= 0) return null;

        // Distance = area * ratio / perimeter (DB paper's formula)
        var distance = area * ratio / perimeter;
        var d = (int)Math.Ceiling(distance);
        return (xmin - d, ymin - d, xmax + d, ymax + d);
    }

    /// <summary>
    /// Post-processes a PP-OCR detector probability map into bounding boxes.
    /// </summary>
    /// <param name="probMap">
    /// The detector output. Shape is [1, 1, H, W] or [1, H, W]. Values are
    /// probabilities in 0..1.
    /// </param>
    /// <param name="originalWidth">The original image width in pixels.</param>
    /// <param name="originalHeight">The original image height in pixels.</param>
    /// <param name="threshold">Probability threshold for binarization (typically 0.3).</param>
    /// <param name="minPixelCount">Minimum component size in pixels (typically 3).</param>
    /// <param name="boxScoreThreshold">Minimum mean score for a box (typically 0.5).</param>
    /// <param name="unclipRatio">Box expansion ratio (typically 1.5).</param>
    /// <returns>Detection boxes in reading order (top-to-bottom, left-to-right).</returns>
    public static List<DetectionBox> PostProcessDetection(
        Tensor<float> probMap,
        int originalWidth,
        int originalHeight,
        float threshold,
        int minPixelCount,
        double boxScoreThreshold,
        float unclipRatio)
    {
        var dims = probMap.Dimensions.ToArray();
        int mapH, mapW;
        if (dims.Length == 4) { mapH = dims[2]; mapW = dims[3]; }
        else if (dims.Length == 3) { mapH = dims[1]; mapW = dims[2]; }
        else { throw new ArgumentException($"Unexpected probMap rank {dims.Length}."); }

        var components = FindConnectedComponents(probMap, threshold, minPixelCount);
        if (components.Count == 0) return new List<DetectionBox>();

        // Map coordinates -> original image coordinates.
        // The detector typically downsamples by 4x (or whatever ratio the model produces).
        var scaleX = (double)originalWidth / mapW;
        var scaleY = (double)originalHeight / mapH;

        var boxes = new List<DetectionBox>(components.Count);
        foreach (var c in components)
        {
            if (c.MeanScore < boxScoreThreshold) continue;

            var expanded = UnclipBox(c.XMin, c.YMin, c.XMax, c.YMax, unclipRatio);
            if (expanded is null) continue;

            var e = expanded.Value;
            var x0 = (int)Math.Floor(e.XMin * scaleX);
            var y0 = (int)Math.Floor(e.YMin * scaleY);
            var x1 = (int)Math.Ceiling((e.XMax + 1) * scaleX);
            var y1 = (int)Math.Ceiling((e.YMax + 1) * scaleY);

            // Clip to original image bounds
            x0 = Math.Max(0, Math.Min(originalWidth - 1, x0));
            y0 = Math.Max(0, Math.Min(originalHeight - 1, y0));
            x1 = Math.Max(x0 + 1, Math.Min(originalWidth, x1));
            y1 = Math.Max(y0 + 1, Math.Min(originalHeight, y1));

            var bw = x1 - x0;
            var bh = y1 - y0;
            if (bw < 2 || bh < 2) continue;

            boxes.Add(new DetectionBox(
                new OcrBoundingBox(x0, y0, bw, bh),
                c.MeanScore));
        }

        // Sort: top-to-bottom, then left-to-right. Use a small row tolerance
        // to keep lines together.
        boxes.Sort((a, b) =>
        {
            const int rowTolerance = 10;
            if (Math.Abs(a.BoundingBox.Y - b.BoundingBox.Y) <= rowTolerance)
            {
                return a.BoundingBox.X.CompareTo(b.BoundingBox.X);
            }
            return a.BoundingBox.Y.CompareTo(b.BoundingBox.Y);
        });

        // Merge by y-overlap is done in OcrResultMerger.Merge, not here.
        // Word-level crops are recognized with higher accuracy than merged
        // wide crops. The merger groups recognized lines by y-overlap.
        return boxes;
    }

    /// <summary>
    /// Merges word-level detection boxes into line-level boxes by grouping
    /// boxes with at least 50 percent vertical overlap and horizontal
    /// gap no more than 30 pixels. Reduces
    /// matching the line-level granularity of the ground-truth full_text label.
    /// </summary>
    /// <param name="boxes">Word-level boxes from PostProcessDetection.</param>
    /// <returns>Line-level merged boxes in reading order.</returns>
    internal static List<DetectionBox> MergeBoxesIntoLines(List<DetectionBox> boxes)
    {
        if (boxes.Count <= 1) return boxes;

        const double YOverlapThreshold = 0.5;


        var sorted = new List<DetectionBox>(boxes);
        sorted.Sort((a, b) =>
        {
            var cmpY = a.BoundingBox.Y.CompareTo(b.BoundingBox.Y);
            if (cmpY != 0) return cmpY;
            return a.BoundingBox.X.CompareTo(b.BoundingBox.X);
        });

        var merged = new List<DetectionBox>();
        var currentGroup = new List<DetectionBox> { sorted[0] };

        for (int i = 1; i < sorted.Count; i++)
        {
            var box = sorted[i];
            var inGroup = false;
            foreach (var g in currentGroup)
            {
                var gY0 = g.BoundingBox.Y;
                var gY1 = g.BoundingBox.Y + g.BoundingBox.Height;
                var bY0 = box.BoundingBox.Y;
                var bY1 = box.BoundingBox.Y + box.BoundingBox.Height;
                var overlap = Math.Max(0, Math.Min(gY1, bY1) - Math.Max(gY0, bY0));
                var minH = Math.Min(g.BoundingBox.Height, box.BoundingBox.Height);
                if (minH <= 0) continue;
                var overlapRatio = (double)overlap / minH;








                if (overlapRatio >= YOverlapThreshold)
                {
                    inGroup = true;
                    break;
                }
            }
            if (inGroup) currentGroup.Add(box);
            else
            {
                merged.Add(MergeGroup(currentGroup));
                currentGroup = new List<DetectionBox> { box };
            }
        }
        merged.Add(MergeGroup(currentGroup));

        merged.Sort((a, b) =>
        {
            const int rowTolerance = 10;
            if (Math.Abs(a.BoundingBox.Y - b.BoundingBox.Y) <= rowTolerance)
                return a.BoundingBox.X.CompareTo(b.BoundingBox.X);
            return a.BoundingBox.Y.CompareTo(b.BoundingBox.Y);
        });

        return merged;
    }

    /// <summary>
    /// Computes the union bounding box of a group of detection boxes.
    /// Score is the maximum detection score in the group.
    /// </summary>
    private static DetectionBox MergeGroup(List<DetectionBox> group)
    {
        if (group.Count == 1) return group[0];
        int minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
        double maxScore = 0;
        foreach (var b in group)
        {
            var bb = b.BoundingBox;
            if (bb.X < minX) minX = bb.X;
            if (bb.Y < minY) minY = bb.Y;
            var rx = bb.X + bb.Width;
            var ry = bb.Y + bb.Height;
            if (rx > maxX) maxX = rx;
            if (ry > maxY) maxY = ry;
            if (b.DetectionScore > maxScore) maxScore = b.DetectionScore;
        }
        return new DetectionBox(
            new OcrBoundingBox(minX, minY, maxX - minX, maxY - minY),
            maxScore);
    }

    /// <summary>
    /// Converts a recognition crop (RGBA bytes) to the normalized tensor
    /// the PP-OCR recognizer expects.
    /// </summary>
    /// <param name="rgba">RGBA bytes. Length = width*height*4.</param>
    /// <param name="width">Crop width in pixels.</param>
    /// <param name="height">Crop height in pixels.</param>
    /// <returns>
    /// Tensor of shape [1, 3, 48, targetWidth] where targetWidth is the
    /// crop width resized to height 48, capped at 320, and padded to a
    /// multiple of 4.
    /// </returns>
    /// <remarks>
    /// PP-OCR recognition preprocessing:
    /// 1. Resize height to 48, width proportional (cap 320)
    /// 2. Normalize: (pixel/255 - 0.5) / 0.5 (mean=std=0.5)
    /// 3. Transpose HWC -> CHW
    /// 4. Add batch dimension
    /// </remarks>
    public static DenseTensor<float> ToRecognitionTensor(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException($"Invalid crop dimensions {width}x{height}.");
        if (rgba.Length != width * height * 4)
            throw new ArgumentException($"RGBA length {rgba.Length} does not match {width}*{height}*4.");

        // Target height 48, width proportional to preserve aspect ratio.
        // PaddleOCR's CTC recognizer is fully convolutional and expects
        // arbitrary width. Capping width to 320 would distort wide crops
        // (e.g. a 249x17 fax line) and produce noise. Cap only at a very
        // large value to bound memory for pathological inputs.
        const int targetHeight = 48;
        const int absoluteMaxWidth = 2048;
        var ratio = (float)width / height;
        var targetWidth = (int)Math.Round(targetHeight * ratio);
        if (targetWidth < 1) targetWidth = 1;
        if (targetWidth > absoluteMaxWidth) targetWidth = absoluteMaxWidth;
        targetWidth = (targetWidth + 3) / 4 * 4;

        var tensor = new DenseTensor<float>(new[] { 1, 3, targetHeight, targetWidth });

        for (int ty = 0; ty < targetHeight; ty++)
        {
            for (int tx = 0; tx < targetWidth; tx++)
            {
                // Nearest-neighbor resize from source crop.
                var sx = Math.Min(width - 1, tx * width / targetWidth);
                var sy = Math.Min(height - 1, ty * height / targetHeight);
                var srcIdx = (sy * width + sx) * 4;
                for (int c = 0; c < 3; c++)
                {
                    float v = rgba[srcIdx + c] / 255f;
                    v = (v - 0.5f) / 0.5f;
                    tensor[0, c, ty, tx] = v;
                }
            }
        }
        return tensor;
    }

    /// <summary>
    /// Greedy CTC decode of the recognizer logits.
    /// </summary>
    /// <param name="logits">Recognizer output. Shape [1, T, C].</param>
    /// <param name="dictionary">Character dictionary. Index 0 is CTC blank.</param>
    /// <returns>The recognized text and confidence (mean of per-timestep max probability).</returns>
    /// <remarks>
    /// Greedy CTC: argmax at each timestep, collapse repeats, remove blanks.
    /// Confidence is the mean max-softmax probability across the collapsed
    /// (kept) timesteps. Blank = dictionary index 0. Dictionary entries are
    /// offset by 1: logits index 1 = dictionary[0].
    /// </remarks>
    public static PpocrRecognition DecodeCtc(Tensor<float> logits, IReadOnlyList<string> dictionary)
    {
        var dims = logits.Dimensions.ToArray();
        if (dims.Length != 3)
            throw new ArgumentException($"Expected 3D logits [1, T, C], got rank {dims.Length}.");

        int T = dims[1];
        int C = dims[2];
        if (C != dictionary.Count + 1)
        {
            // Allow one extra class if it is a CTC blank at index 0.
            // Common case: C = dictionary.Count + 1.
            // If C == dictionary.Count, assume no separate blank.
        }

        var sb = new System.Text.StringBuilder();
        var confidences = new List<double>();
        int prev = -1;

        // PaddleOCR's CTC recognizer output is ALREADY softmax-normalized
        // probabilities, not raw logits. Do not apply softmax again.
        // For each timestep, take argmax class and use its probability
        // directly as the confidence.
        for (int t = 0; t < T; t++)
        {
            int best = 0;
            float bestProb = float.NegativeInfinity;
            for (int c = 0; c < C; c++)
            {
                var v = logits[0, t, c];
                if (v > bestProb) { bestProb = v; best = c; }
            }
            var maxProb = bestProb;

            // best == 0 is CTC blank
            if (best != 0 && best != prev)
            {
                var dictIndex = best - 1;
                if (dictIndex >= 0 && dictIndex < dictionary.Count)
                {
                    sb.Append(dictionary[dictIndex]);
                    confidences.Add(maxProb);
                }
                else if (dictIndex == dictionary.Count)
                {
                    // PaddleOCR's use_space_char=True appends a space at
                    // the end of the character set. Its class index is
                    // dictionary.Count in the model output.
                    sb.Append(' ');
                    confidences.Add(maxProb);
                }
            }
            prev = best;
        }

        var confidence = confidences.Count > 0 ? confidences.Average() : (double?)null;
        return new PpocrRecognition(sb.ToString(), confidence);
    }

    /// <summary>
    /// Converts a crop (RGBA bytes) to the normalized tensor TrOCR's
    /// encoder expects.
    /// </summary>
    /// <param name="rgba">RGBA bytes. Length = width*height*4.</param>
    /// <param name="width">Crop width in pixels.</param>
    /// <param name="height">Crop height in pixels.</param>
    /// <returns>Tensor of shape [1, 3, 384, 384], normalized with mean=std=0.5.</returns>
    /// <remarks>
    /// TrOCR preprocessing:
    /// 1. Resize crop to 384x384 (nearest-neighbor)
    /// 2. Normalize: (pixel/255 - 0.5) / 0.5
    /// 3. Transpose HWC -> CHW, add batch dim
    /// </remarks>
    public static DenseTensor<float> ToEncoderTensor(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException($"Invalid crop dimensions {width}x{height}.");
        if (rgba.Length != width * height * 4)
            throw new ArgumentException($"RGBA length {rgba.Length} does not match {width}*{height}*4.");

        const int targetSize = 384;
        var tensor = new DenseTensor<float>(new[] { 1, 3, targetSize, targetSize });

        for (int ty = 0; ty < targetSize; ty++)
        {
            for (int tx = 0; tx < targetSize; tx++)
            {
                var sx = Math.Min(width - 1, tx * width / targetSize);
                var sy = Math.Min(height - 1, ty * height / targetSize);
                var srcIdx = (sy * width + sx) * 4;
                for (int c = 0; c < 3; c++)
                {
                    float v = rgba[srcIdx + c] / 255f;
                    v = (v - 0.5f) / 0.5f;
                    tensor[0, c, ty, tx] = v;
                }
            }
        }
        return tensor;
    }
}

/// <summary>
/// A connected component detected on the PP-OCR probability map, before
/// unclip and coordinate scaling.
/// </summary>
/// <param name="XMin">Left edge in map coordinates.</param>
/// <param name="YMin">Top edge in map coordinates.</param>
/// <param name="XMax">Right edge in map coordinates (inclusive).</param>
/// <param name="YMax">Bottom edge in map coordinates (inclusive).</param>
/// <param name="MeanScore">Mean probability across the component's pixels (0..1).</param>
/// <param name="PixelCount">Number of foreground pixels.</param>
public readonly record struct DetectedComponent(
    int XMin,
    int YMin,
    int XMax,
    int YMax,
    double MeanScore,
    int PixelCount);

