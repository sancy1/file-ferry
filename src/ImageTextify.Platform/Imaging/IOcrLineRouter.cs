// filepath: src/ImageTextify.Platform/Imaging/IOcrLineRouter.cs
// layer:    Infrastructure (interface)
// module:   ImageTextify.Platform.Imaging
// since:    Phase 2.4b2
// purpose:  Decides whether a line's PP-OCR text should be replaced by
//           TrOCR handwriting recognition. Seam for testing the routing
//           decision in isolation, without loading ONNX models.
//
// RELATIONSHIPS
//   Implements : n/a (interface)
//   Depends on : n/a (BCL only)
//   Used by    : ImageTextify.Platform.OnnxOcrEngine (consumer)
//   See also   : OcrLineRouter.cs, OnnxOcrEngine.cs
//
// NOTES
//   - The router implements decision D1 from the Phase 2.4b2 design plan:
//     a simplified "PP-OCR first, TrOCR fallback on visible garbage" rule.
//     The confidence-band router from Phase 2.4a was rejected because the
//     eval showed it over-triggers on faint handwriting where PP-OCR is
//     correct.
//   - TrOCR is a fallback, not a co-equal engine. When PP-OCR produces
//     plausible text, it wins. TrOCR runs only when PP-OCR produces
//     nothing or obviously broken output.

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// Decides whether a detected line should be routed to TrOCR.
/// </summary>
public interface IOcrLineRouter
{
    /// <summary>
    /// Returns true when the line's PP-OCR result should be discarded in
    /// favour of TrOCR.
    /// </summary>
    /// <param name="ppocrConfidence">
    /// The PP-OCR recognizer's confidence in the range 0-1, or null when
    /// the recognizer did not report one. Null is treated as a routing
    /// signal toward TrOCR.
    /// </param>
    /// <param name="ppocrText">
    /// The PP-OCR recognized text. Empty or whitespace routes to TrOCR.
    /// Never null.
    /// </param>
    /// <param name="cropWidth">
    /// The line crop's width in pixels. Used for length-plausibility
    /// heuristics. Must be positive.
    /// </param>
    /// <param name="cropHeight">
    /// The line crop's height in pixels. Reserved for future heuristics.
    /// Must be positive.
    /// </param>
    /// <returns>True if TrOCR should be attempted, false to keep PP-OCR text.</returns>
    bool ShouldRouteToTrocr(
        double? ppocrConfidence,
        string ppocrText,
        int cropWidth,
        int cropHeight);
}