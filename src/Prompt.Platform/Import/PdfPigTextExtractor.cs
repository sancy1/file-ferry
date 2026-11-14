// ============================================================================
// FILE:     src/Prompt.Platform/Import/PdfPigTextExtractor.cs
// PURPOSE:  The PdfPig-backed IPdfTextExtractor. Opens the PDF with PdfPig,
//           walks each page, and concatenates the reading-order text
//           (ContentOrderTextExtractor) into one string.
// RELATION: Implements IPdfTextExtractor (Prompt.Core.Domain). Consumed by
//           PromptImporter (P3.5). The PdfPig package (0.1.16) is referenced
//           by Prompt.Platform.
// NOTES:    - The reading-order extraction is the recommended PdfPig API;
//             the raw page.Text preserves the content-stream order, which is
//             not the reading order.
//           - The extraction is CPU-bound; it runs on Task.Run so the caller's
//             async context is not blocked.
//           - A malformed PDF throws; the exception propagates to the
//             importer, which converts it into ImportResult.Fail.
// ============================================================================

using System.Text;
using Prompt.Core.Domain;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Prompt.Platform.Import;

/// <summary>
/// A PdfPig-backed PDF text extractor.
/// </summary>
public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    /// <inheritdoc/>
    public Task<string> ExtractTextAsync(string pdfFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfFilePath);
        return Task.Run(() =>
        {
            using var document = PdfDocument.Open(pdfFilePath);
            var sb = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine(ContentOrderTextExtractor.GetText(page));
            }
            return sb.ToString();
        }, cancellationToken);
    }
}