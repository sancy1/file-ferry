// filepath: src/ImageTextify.Platform/Imaging/IOcrResultMerger.cs
// layer:    Infrastructure (interface)
// module:   ImageTextify.Platform.Imaging
// since:    Phase 2.4b2
// purpose:  Merges per-line OcrLineResult into an ordered OcrResult.
//           Uses the existing ReadingOrder.Sort for multi-column layouts.
//           Seam for testing merge logic in isolation.
//
// RELATIONSHIPS
//   Implements : n/a (interface)
//   Depends on : ImageTextify.Core.Abstractions.OcrLineResult
//                ImageTextify.Core.Abstractions.OcrResult
//                ImageTextify.Core.Abstractions.OcrTextKind
//   Used by    : ImageTextify.Platform.OnnxOcrEngine (consumer)
//   See also   : OcrResultMerger.cs, ReadingOrder.cs
//
// NOTES
//   - Reading order is determined by bounding-box geometry via
//     ReadingOrder.Sort. The sort handles single-column, two-column,
//     and multi-column layouts.
//   - Line breaks are preserved in the merged text. Empty lines (zero
//     text) are dropped from both text and the line list.
//   - Overall confidence is the mean of per-line confidences where
//     non-null. If no line reports a confidence, the overall is null.

using ImageTextify.Core.Abstractions;

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// Merges per-line OCR results into a final OcrResult in reading order.
/// </summary>
public interface IOcrResultMerger
{
    /// <summary>
    /// Merges the given lines into an OcrResult in reading order.
    /// </summary>
    /// <param name="lines">
    /// Per-line results in detection order. Not null; may be empty.
    /// </param>
    /// <param name="warnings">
    /// Quality warnings from the extraction pipeline. Not null; may be empty.
    /// </param>
    /// <returns>
    /// The merged result with ordered text, overall confidence,
    /// detected text kind, and per-line provenance.
    /// </returns>
    OcrResult Merge(IReadOnlyList<OcrLineResult> lines, IReadOnlyList<string> warnings);
}