// filepath: src/ImageTextify.Platform/Imaging/IOcrModelRunner.cs
// layer:    Infrastructure (interface)
// module:   ImageTextify.Platform.Imaging
// since:    Phase 2.4b2
// purpose:  Testability seam for ONNX model inference. OnnxOcrEngine
//           depends on this interface, not on OnnxModelRunner directly.
//           Tests for OcrLineRouter and OcrResultMerger use a fake
//           implementation without loading ONNX models.
//
// RELATIONSHIPS
//   Implements : n/a (interface)
//   Depends on : ImageTextify.Core.Abstractions.OcrBoundingBox
//   Used by    : ImageTextify.Platform.OnnxOcrEngine (consumer),
//                ImageTextify.Platform.OnnxModelRunner (real impl),
//                FileHarvester.Tests.OcrLineRouterTests,
//                FileHarvester.Tests.OcrResultMergerTests,
//                FileHarvester.Tests.OnnxOcrEngineTests
//   See also   : ImageTextify.Platform.OnnxModelRunner
//
// NOTES
//   - PP-OCR recognition returns text with confidence (PP-OCR exposes
//     a CTC-based confidence per line).
//   - TrOCR recognition returns text only. TrOCR does not expose a
//     calibrated per-line confidence; models return null for that
//     reason.
//   - Detection boxes are axis-aligned in input image pixel coordinates.
//   - The interface is intentionally small. If 2.4b3 needs additional
//     inference capabilities, extend this interface explicitly; do not
//     add optional methods that implementations can ignore.

using ImageTextify.Core.Abstractions;

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// Runs ONNX model inference for the OCR pipeline. The seam between
/// the OCR engine orchestrator and the ONNX Runtime calls.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are not thread-safe. Each inference call may be
/// invoked on any thread, but concurrent invocations on the same
/// instance are undefined behavior. The OCR engine invokes inference
/// sequentially per image and disposes the runner when done.
/// </para>
/// <para>
/// All methods honor the cancellation token. Cancellation surfaces as
/// <see cref="OperationCanceledException"/>. No partial state is left
/// behind on cancellation.
/// </para>
/// </remarks>
public interface IOcrModelRunner : IAsyncDisposable
{
    /// <summary>
    /// Runs PP-OCR text detection on the image.
    /// </summary>
    /// <param name="imageRgbaBytes">
    /// The image as RGBA bytes. Length must be Width*Height*4.
    /// </param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="cancellationToken">Cancels the inference.</param>
    /// <returns>
    /// Detection boxes in reading order (top-to-bottom, left-to-right).
    /// Empty when no text is detected.
    /// </returns>
    Task<IReadOnlyList<DetectionBox>> RunDetectionAsync(
        ReadOnlyMemory<byte> imageRgbaBytes,
        int width,
        int height,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs PP-OCR text recognition on a single line crop.
    /// </summary>
    /// <param name="cropRgbaBytes">
    /// The line crop as RGBA bytes. Length must be CropWidth*CropHeight*4.
    /// </param>
    /// <param name="cropWidth">Crop width in pixels.</param>
    /// <param name="cropHeight">Crop height in pixels.</param>
    /// <param name="cancellationToken">Cancels the inference.</param>
    /// <returns>
    /// The recognized text and its confidence. Empty text and null
    /// confidence when recognition fails.
    /// </returns>
    Task<PpocrRecognition> RunPpocrRecognitionAsync(
        ReadOnlyMemory<byte> cropRgbaBytes,
        int cropWidth,
        int cropHeight,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs TrOCR handwritten recognition on a single line crop.
    /// </summary>
    /// <param name="cropRgbaBytes">
    /// The line crop as RGBA bytes. Length must be CropWidth*CropHeight*4.
    /// </param>
    /// <param name="cropWidth">Crop width in pixels.</param>
    /// <param name="cropHeight">Crop height in pixels.</param>
    /// <param name="cancellationToken">Cancels the inference.</param>
    /// <returns>
    /// The recognized text. Empty when recognition fails. TrOCR does
    /// not expose a calibrated per-line confidence, so there is no
    /// confidence return value.
    /// </returns>
    Task<string> RunTrocrRecognitionAsync(
        ReadOnlyMemory<byte> cropRgbaBytes,
        int cropWidth,
        int cropHeight,
        CancellationToken cancellationToken);
}

/// <summary>
/// A detected text region from PP-OCR detection.
/// </summary>
/// <param name="BoundingBox">
/// The bounding box in the input image's pixel coordinates.
/// </param>
/// <param name="DetectionScore">
/// The detector's confidence in this region, in the range zero to one.
/// </param>
public sealed record DetectionBox(
    OcrBoundingBox BoundingBox,
    double DetectionScore);

/// <summary>
/// The result of a PP-OCR recognition call on one line crop.
/// </summary>
/// <param name="Text">
/// The recognized text. Never null; empty when recognition fails.
/// </param>
/// <param name="Confidence">
/// The recognition confidence in the range zero to one, or null when
/// the model did not produce a confidence (rare for PP-OCR; present
/// in normal cases).
/// </param>
public sealed record PpocrRecognition(
    string Text,
    double? Confidence);