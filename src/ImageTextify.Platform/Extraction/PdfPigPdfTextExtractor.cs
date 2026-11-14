// filepath: src/ImageTextify.Platform/Extraction/PdfPigPdfTextExtractor.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Extraction
// since:    Phase 2.6-PDF-1
// purpose:  PdfPig-backed implementation of IPdfTextExtractor for the
//           digital-PDF path. Reconstructs page text from word positions
//           so line breaks, word spacing, and paragraph gaps are
//           preserved, instead of using the content-order extractor which
//           normalizes whitespace.
// RELATION: Implements IPdfTextExtractor (ImageTextify.Core.Abstractions).
//           Registered in AddImageTextify. Consumed by
//           ImageTextifyViewModel.ExtractAllAsync for the Pdf category.
// NOTES:    - Groups words into lines by vertical position, joins words
//             within a line with a single space, joins lines with a newline,
//             and inserts a blank line where the vertical gap between lines
//             exceeds a paragraph threshold.
//           - Word spacing that the PDF encodes inside a single word run is
//             not recoverable; PdfPig exposes one Word.Text per glyph run.
//             Tabs and column alignment are not restored.
//           - WasOcred is always false in this phase: no OCR runs on PDF
//             pages.
//           - The caller owns the stream; this class does not dispose it.
//             The PdfPig document is disposed.
//           - Encryption and malformed-PDF exceptions are not mapped here;
//             they propagate and the caller turns them into a failed row.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace ImageTextify.Platform.Extraction;

/// <summary>
/// Extracts the embedded text layer from a digital PDF, preserving line
/// breaks, word spacing, and paragraph gaps by reconstructing text from
/// word positions.
/// </summary>
public sealed class PdfPigPdfTextExtractor : IPdfTextExtractor
{
    /// <inheritdoc/>
    public Task<PdfTextResult> ExtractAsync(
        Stream pdfStream,
        PdfExtractionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdfStream);
        ArgumentNullException.ThrowIfNull(options);

        return Task.Run(() =>
        {
            using var document = PdfDocument.Open(pdfStream);
            var pages = new List<PdfPageText>();
            var wanted = options.PageIndexes;
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (wanted is not null && !wanted.Contains(page.Number - 1)) continue;
                var text = BuildPageText(page);
                pages.Add(new PdfPageText(page.Number - 1, text, WasOcred: false));
            }
            return new PdfTextResult(pages);
        }, cancellationToken);
    }

    /// <summary>
    /// Reconstructs a page's text from its words and their positions.
    /// </summary>
    private static string BuildPageText(Page page)
    {
        var words = page.GetWords().ToList();
        if (words.Count == 0) return string.Empty;

        // Group by vertical band. PDF Y increases upward; sort top-down.
        var sorted = words.OrderByDescending(w => w.BoundingBox.Bottom).ToList();
        var lines = new List<List<Word>>();
        var current = new List<Word> { sorted[0] };
        var currentBottom = sorted[0].BoundingBox.Bottom;
        for (var i = 1; i < sorted.Count; i++)
        {
            var w = sorted[i];
            var h = Math.Max(w.BoundingBox.Height, 1.0);
            if (Math.Abs(w.BoundingBox.Bottom - currentBottom) <= h * 0.5)
            {
                current.Add(w);
            }
            else
            {
                lines.Add(current);
                current = new List<Word> { w };
                currentBottom = w.BoundingBox.Bottom;
            }
        }
        lines.Add(current);

        // Horizontal spacing: measure each line's inter-word gaps and use
        // the page median as the space width. A gap of N space-widths is
        // reproduced as N spaces; a normal gap is one space. Nothing is
        // collapsed to a single space and nothing is invented.
        var allGaps = new List<double>();
        foreach (var line in lines)
        {
            var ordered = line.OrderBy(w => w.BoundingBox.Left).ToList();
            for (var k = 1; k < ordered.Count; k++)
            {
                var gap = ordered[k].BoundingBox.Left - (ordered[k - 1].BoundingBox.Left + ordered[k - 1].BoundingBox.Width);
                if (gap > 0) allGaps.Add(gap);
            }
        }
        var spaceWidth = Median(allGaps);
        if (spaceWidth <= 0) spaceWidth = 3.0;

        // Vertical spacing: measure each consecutive line gap and use the
        // page median as the line pitch. Extra blank lines are emitted only
        // when the measured gap is a multiple of the pitch greater than one.
        var lineGaps = new List<double>();
        for (var i = 1; i < lines.Count; i++)
        {
            var prevBottom = lines[i - 1].Min(w => w.BoundingBox.Bottom);
            var nextBottom = lines[i].Min(w => w.BoundingBox.Bottom);
            var gap = prevBottom - nextBottom;
            if (gap > 0) lineGaps.Add(gap);
        }
        var linePitch = Median(lineGaps);
        if (linePitch <= 0) linePitch = 12.0;

        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].OrderBy(w => w.BoundingBox.Left).ToList();
            for (var k = 0; k < line.Count; k++)
            {
                if (k > 0)
                {
                    var gap = line[k].BoundingBox.Left - (line[k - 1].BoundingBox.Left + line[k - 1].BoundingBox.Width);
                    var spaces = gap > 0 ? (int)Math.Max(1, Math.Round(gap / spaceWidth)) : 1;
                    sb.Append(' ', spaces);
                }
                sb.Append(line[k].Text);
            }
            if (i < lines.Count - 1)
            {
                var prevBottom = line.Min(w => w.BoundingBox.Bottom);
                var nextLine = lines[i + 1];
                var nextBottom = nextLine.Min(w => w.BoundingBox.Bottom);
                var gap = prevBottom - nextBottom;
                sb.Append('\n');
                var extra = gap > 0 ? (int)Math.Max(0, Math.Round(gap / linePitch) - 1) : 0;
                for (var e = 0; e < extra; e++) sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Median of a list of doubles. Returns zero for an empty list.
    /// </summary>
    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 0.0;
        var ordered = values.OrderBy(v => v).ToList();
        var mid = ordered.Count / 2;
        return ordered.Count % 2 == 0 ? (ordered[mid - 1] + ordered[mid]) / 2.0 : ordered[mid];
    }
}
