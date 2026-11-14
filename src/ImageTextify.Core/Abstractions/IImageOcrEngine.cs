// filepath: src/ImageTextify.Core/Abstractions/IImageOcrEngine.cs
// layer:    Contracts
// module:   ImageTextify.Core.Abstractions
// since:    Phase 2.4b2
// purpose:  Contract for the OCR engine. Stream-based; the caller owns
//           the stream. The engine reports the detected text kind and
//           per-line provenance so handwriting is never silently
//           treated as printed text and every line is attributable to
//           the engine that produced it.
//
// RELATIONSHIPS
//   Implements : n/a (interface)
//   Depends on : n/a (BCL only)
//   Used by    : ImageTextify.Core.IImagePreprocessor (consumer),
//                ImageTextify.Platform.OnnxOcrEngine (implementation),
//                FileHarvester.Tests.CoreContractsTests (shape checks)
//   See also   : ImageTextify.Core.Abstractions.IImagePreprocessor,
//                ImageTextify.Core.Abstractions.IModelStore
//
// NOTES
//   - OcrResult carries Lines with per-line provenance (Reviewer 1 fix #9).
//   - OcrResult is a public record. Adding a parameter is a breaking change.
//     No external constructors exist as of Phase 2.4b2.
//   - OcrBoundingBox uses int pixel coordinates. SkiaSharp int types map
//     directly.
//   - OcrEngineKind is intentionally an enum (not a string) so the closed
//     set of engines is enforced at the type level.

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// Extracts text from an image stream.
/// </summary>
public interface IImageOcrEngine
{
    /// <summary>
    /// Extracts text from an image.
    /// </summary>
    /// <param name="imageStream">
    /// The image bytes. The caller owns the stream; the engine must not
    /// close it. Null is rejected by the implementation.
    /// </param>
    /// <param name="options">
    /// Extraction options. Not null.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the extraction. Implementations honor the token promptly
    /// and leave no partial state.
    /// </param>
    /// <returns>
    /// The extracted text, confidence when reported, quality warnings,
    /// the detected text kind, and per-line provenance.
    /// </returns>
    Task<OcrResult> ExtractAsync(
        Stream imageStream,
        ImageOcrOptions options,
        CancellationToken cancellationToken);
}

/// <summary>
/// Options controlling OCR extraction.
/// </summary>
/// <param name="Enhance">
/// When true, apply the enhancement pipeline (orientation, deskew,
/// denoise, contrast, sharpen, binarize). Default true.
/// </param>
/// <param name="DetectOrientation">
/// When true, detect and correct orientation before recognition.
/// Default true.
/// </param>
/// <param name="AttemptHandwritingRecognition">
/// When true, attempt handwriting recognition in addition to printed
/// text. Default false. Handwriting is a separate recognition concern;
/// enabling this must never cause the engine to silently misread
/// handwriting as printed text.
/// </param>
public sealed record ImageOcrOptions(
    bool Enhance = true,
    bool DetectOrientation = true,
    bool AttemptHandwritingRecognition = false);

/// <summary>
/// The result of an OCR call.
/// </summary>
/// <param name="Text">
/// The extracted text. Never null; may be empty when no text was
/// detected. Uncertain regions use the literal token [unclear].
/// </param>
/// <param name="Confidence">
/// The engine-reported confidence in the range zero to one, or null
/// when the engine does not report one.
/// </param>
/// <param name="Warnings">
/// Quality warnings: low resolution, motion blur, poor contrast,
/// glare, skew beyond the correction range, low confidence, suspected
/// handwriting, no text detected. Never null; empty when none apply.
/// </param>
/// <param name="DetectedTextKind">
/// The kind of text detected. Used by the UI to warn when handwriting
/// was recognized with low confidence rather than pretending it is
/// printed.
/// </param>
/// <param name="Lines">
/// Per-line results with provenance. Never null; empty when no text
/// was detected. Each line records which engine produced it (PP-OCR,
/// TrOCR, or TrOCR whole-image fallback) so the UI can attribute
/// results and the corrections store can track per-engine accuracy.
/// </param>
public sealed record OcrResult(
    string Text,
    double? Confidence,
    IReadOnlyList<string> Warnings,
    OcrTextKind DetectedTextKind,
    IReadOnlyList<OcrLineResult> Lines);

/// <summary>
/// One recognized line from an OCR extraction.
/// </summary>
/// <param name="Text">
/// The recognized text. Never null; may be empty when the line was
/// detected but not recognized.
/// </param>
/// <param name="BoundingBox">
/// The line's axis-aligned bounding box in the input image's pixel
/// coordinates.
/// </param>
/// <param name="Confidence">
/// The engine-reported confidence in the range zero to one, or null
/// when the engine does not report a per-line confidence (for example,
/// TrOCR does not expose a calibrated per-line confidence).
/// </param>
/// <param name="Engine">
/// The engine that produced this line. Used for per-line provenance
/// and per-engine calibration in Phase 2.9.
/// </param>
public sealed record OcrLineResult(
    string Text,
    OcrBoundingBox BoundingBox,
    double? Confidence,
    OcrEngineKind Engine);

/// <summary>
/// Axis-aligned bounding box in pixel coordinates.
/// </summary>
/// <param name="X">The left edge in pixels.</param>
/// <param name="Y">The top edge in pixels.</param>
/// <param name="Width">The width in pixels. Positive.</param>
/// <param name="Height">The height in pixels. Positive.</param>
public readonly record struct OcrBoundingBox(int X, int Y, int Width, int Height);

/// <summary>
/// The kind of text detected by the OCR engine.
/// </summary>
public enum OcrTextKind
{
    /// <summary>
    /// Printed or typed text. The default for a clear input.
    /// </summary>
    Printed = 0,

    /// <summary>
    /// Handwritten text. Reported only when the engine is confident.
    /// </summary>
    Handwritten = 1,

    /// <summary>
    /// A mix of printed and handwritten text on the same input.
    /// </summary>
    Mixed = 2,

    /// <summary>
    /// The engine could not confidently classify the text kind.
    /// </summary>
    Unknown = 3,
}

/// <summary>
/// The OCR engine that produced a line. Used for per-line provenance.
/// </summary>
public enum OcrEngineKind
{
    /// <summary>
    /// PP-OCR recognition (printed text primary path).
    /// </summary>
    Ppocr = 0,

    /// <summary>
    /// TrOCR handwriting recognition (per-line fallback).
    /// </summary>
    Trocr = 1,

    /// <summary>
    /// TrOCR on the whole image (whole-image fallback for rotated or
    /// boxless images).
    /// </summary>
    TrocrWholeImage = 2,

    /// <summary>
    /// Qwen printed-text fallback (opt-in per-line fallback for
    /// low-confidence printed text; see OnnxOcrEngine).
    /// </summary>
    QwenPrinted = 3,
}
