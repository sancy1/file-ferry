// filepath: src/FileHarvester.Tests/ImageTextify/FakeOcrModelRunner.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 2.4b2
// purpose:  Test double for IOcrModelRunner. Provides configurable,
//           immutable behavior so router, merger, and engine tests
//           can exercise pipeline logic without loading real ONNX
//           models. Detection, PP-OCR recognition, and TrOCR
//           recognition return the values passed to the constructor.
//
// RELATIONSHIPS
//   Implements : ImageTextify.Platform.Imaging.IOcrModelRunner
//   Depends on : n/a (test helper)
//   Used by    : OcrLineRouterTests, OcrResultMergerTests,
//                OnnxOcrEngineTests
//   See also   : IOcrModelRunner.cs, OnnxOcrEngine.cs
//
// NOTES
//   - Immutable. All behavior set via constructor. No properties to
//     mutate after construction. Tests cannot accidentally change
//     behavior mid-test.
//   - Tracks call counts (DetectionCalls, PpocrCalls, TrocrCalls) for
//     tests that need to verify invocation counts. Counts are updated
//     per call. Not thread-safe (tests run single-threaded).
//   - If a per-call override dictionary is provided, the fake returns
//     the override for that specific call index. Otherwise returns the
//     default from the constructor. Useful for testing that a specific
//     crop returns specific text.
//   - DisposeAsync is a no-op. This is a test double; there are no
//     sessions to dispose.

using ImageTextify.Core.Abstractions;
using ImageTextify.Platform.Imaging;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Test double for IOcrModelRunner with configurable, immutable behavior.
/// </summary>
public sealed class FakeOcrModelRunner : IOcrModelRunner
{
    private readonly IReadOnlyList<DetectionBox> _detectionBoxes;
    private readonly string _ppocrText;
    private readonly double? _ppocrConfidence;
    private readonly string _trocrText;
    private readonly IReadOnlyDictionary<int, PpocrRecognition>? _ppocrPerCall;
    private readonly IReadOnlyDictionary<int, string>? _trocrPerCall;

    /// <summary>
    /// Number of times RunDetectionAsync has been called.
    /// </summary>
    public int DetectionCalls { get; private set; }

    /// <summary>
    /// Number of times RunPpocrRecognitionAsync has been called.
    /// </summary>
    public int PpocrCalls { get; private set; }

    /// <summary>
    /// Number of times RunTrocrRecognitionAsync has been called.
    /// </summary>
    public int TrocrCalls { get; private set; }

    /// <summary>
    /// Creates a fake with default behavior for all calls.
    /// </summary>
    /// <param name="detectionBoxes">
    /// The boxes returned by every detection call. Not null; may be empty.
    /// </param>
    /// <param name="ppocrText">The text returned by every PP-OCR call. Not null.</param>
    /// <param name="ppocrConfidence">The PP-OCR confidence. May be null.</param>
    /// <param name="trocrText">The text returned by every TrOCR call. Not null.</param>
    public FakeOcrModelRunner(
        IReadOnlyList<DetectionBox> detectionBoxes,
        string ppocrText,
        double? ppocrConfidence,
        string trocrText)
    {
        _detectionBoxes = detectionBoxes ?? throw new ArgumentNullException(nameof(detectionBoxes));
        _ppocrText = ppocrText ?? throw new ArgumentNullException(nameof(ppocrText));
        _ppocrConfidence = ppocrConfidence;
        _trocrText = trocrText ?? throw new ArgumentNullException(nameof(trocrText));
    }

    /// <summary>
    /// Creates a fake with per-call overrides for PP-OCR and TrOCR.
    /// Detection is still controlled by <paramref name="detectionBoxes"/>.
    /// </summary>
    /// <param name="detectionBoxes">The boxes returned by every detection call.</param>
    /// <param name="ppocrPerCall">
    /// Per-call PP-OCR recognition overrides. Call index is zero-based.
    /// Missing indices fall back to <paramref name="defaultPpocr"/>.
    /// </param>
    /// <param name="trocrPerCall">
    /// Per-call TrOCR recognition overrides. Missing indices fall back
    /// to <paramref name="defaultTrocr"/>.
    /// </param>
    /// <param name="defaultPpocr">Default PP-OCR result when no override is set.</param>
    /// <param name="defaultTrocr">Default TrOCR text when no override is set.</param>
    public FakeOcrModelRunner(
        IReadOnlyList<DetectionBox> detectionBoxes,
        IReadOnlyDictionary<int, PpocrRecognition> ppocrPerCall,
        IReadOnlyDictionary<int, string> trocrPerCall,
        PpocrRecognition defaultPpocr,
        string defaultTrocr)
        : this(detectionBoxes, defaultPpocr.Text, defaultPpocr.Confidence, defaultTrocr)
    {
        _ppocrPerCall = ppocrPerCall;
        _trocrPerCall = trocrPerCall;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<DetectionBox>> RunDetectionAsync(
        ReadOnlyMemory<byte> imageRgbaBytes,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DetectionCalls++;
        return Task.FromResult(_detectionBoxes);
    }

    /// <inheritdoc/>
    public Task<PpocrRecognition> RunPpocrRecognitionAsync(
        ReadOnlyMemory<byte> cropRgbaBytes,
        int cropWidth,
        int cropHeight,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var index = PpocrCalls;
        PpocrCalls++;
        if (_ppocrPerCall is not null && _ppocrPerCall.TryGetValue(index, out var overrideResult))
        {
            return Task.FromResult(overrideResult);
        }
        return Task.FromResult(new PpocrRecognition(_ppocrText, _ppocrConfidence));
    }

    /// <inheritdoc/>
    public Task<string> RunTrocrRecognitionAsync(
        ReadOnlyMemory<byte> cropRgbaBytes,
        int cropWidth,
        int cropHeight,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var index = TrocrCalls;
        TrocrCalls++;
        if (_trocrPerCall is not null && _trocrPerCall.TryGetValue(index, out var overrideText))
        {
            return Task.FromResult(overrideText);
        }
        return Task.FromResult(_trocrText);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}