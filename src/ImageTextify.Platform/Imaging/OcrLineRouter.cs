// filepath: src/ImageTextify.Platform/Imaging/OcrLineRouter.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Imaging
// since:    Phase 2.4b2
// purpose:  Implements the simplified "PP-OCR first, TrOCR fallback on
//           visible garbage" routing rule. Pure logic, no ONNX, no I/O.
//           Testable with synthetic inputs.
//
// RELATIONSHIPS
//   Implements : ImageTextify.Platform.Imaging.IOcrLineRouter
//   Depends on : n/a (BCL only)
//   Used by    : ImageTextify.Platform.OnnxOcrEngine
//   See also   : IOcrLineRouter.cs, OnnxOcrEngine.cs
//
// NOTES
//   - Decision D1 from the Phase 2.4b2 design plan.
//   - Six garbage heuristics determine whether PP-OCR output is "visible
//     garbage" that TrOCR should attempt to replace:
//       1. Empty or whitespace text
//       2. Single character where the crop is wide enough to expect more
//       3. Over 80% non-alphanumeric characters
//       4. Repeated-character garbage (aaaaaa, xxxx, ....)
//       5. Too short for the crop width (30 px per char minimum)
//       6. Too long for the crop width (2 px per char maximum)
//   - If any heuristic fires, the line is routed to TrOCR. Otherwise
//     PP-OCR text is kept.
//   - The router does not consider confidence. Confidence bands were
//     rejected during Phase 2.4b2 design because they over-trigger on
//     faint handwriting where PP-OCR is correct.

using System.Text.RegularExpressions;

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// Confidence-independent line router. Routes to TrOCR only when
/// PP-OCR output is visibly garbage.
/// </summary>
public sealed class OcrLineRouter : IOcrLineRouter
{
    private static readonly Regex RepeatedCharRegex =
        new(@"^(.)\1{4,}$", RegexOptions.Compiled);

    /// <inheritdoc/>
    public bool ShouldRouteToTrocr(
        double? ppocrConfidence,
        string ppocrText,
        int cropWidth,
        int cropHeight)
    {
        if (cropWidth <= 0 || cropHeight <= 0)
        {
            throw new ArgumentException(
                $"Crop dimensions must be positive. Got {cropWidth}x{cropHeight}.");
        }

        // Heuristic 1: null confidence. Not a hard requirement of the
        // recognizer, but if the recognizer produced no confidence, treat
        // it as uncertain.
        if (ppocrConfidence is null)
        {
            return true;
        }

        // Heuristic 1 (cont.): empty or whitespace text
        if (string.IsNullOrWhiteSpace(ppocrText))
        {
            return true;
        }

        var text = ppocrText.Trim();

        // Heuristic 4: repeated-character garbage
        if (RepeatedCharRegex.IsMatch(text))
        {
            return true;
        }

        // Heuristic 3: over 80% non-alphanumeric
        var nonAlnum = 0;
        var alnum = 0;
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch)) alnum++;
            else nonAlnum++;
        }
        var total = alnum + nonAlnum;
        if (total > 0 && (double)nonAlnum / total > 0.80)
        {
            return true;
        }

        // Heuristic 5: too short for crop width (30 px per char minimum)
        var expectedMinChars = Math.Max(2, cropWidth / 30);
        if (text.Length < expectedMinChars && text.Length < 4)
        {
            // Small tolerance: very short text in a narrow crop is OK.
            // Only fires when the crop is clearly wide enough to hold more.
            if (cropWidth >= 120)
            {
                return true;
            }
        }

        // Heuristic 6: too long for crop width (2 px per char maximum)
        if (text.Length > cropWidth / 2)
        {
            return true;
        }

        return false;
    }
}