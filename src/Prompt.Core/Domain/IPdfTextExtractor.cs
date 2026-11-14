// ============================================================================
// FILE:     src/Prompt.Core/Domain/IPdfTextExtractor.cs
// PURPOSE:  Contract for extracting the text from a PDF file. Abstracted so
//           the importer does not depend on the PdfPig package directly; the
//           real PdfPigTextExtractor lives in Prompt.Platform.Import; the
//           tests substitute this interface.
// RELATION: Implemented by PdfPigTextExtractor (P3.5, Prompt.Platform.Import).
//           Consumed by PromptImporter (P3.5). The importer's .pdf path calls
//           this; the .txt/.md/.docx paths are direct.
// NOTES:    - The extraction is best-effort; a malformed PDF throws and the
//             importer converts the exception into ImportResult.Fail.
//           - The extracted text is the reading-order text (PdfPig's
//             ContentOrderTextExtractor), not the raw content stream order.
// ============================================================================

namespace Prompt.Core.Domain;

/// <summary>
/// Extracts text from a PDF file.
/// </summary>
public interface IPdfTextExtractor
{
    /// <summary>
    /// Extracts the text from the PDF at <paramref name="pdfFilePath"/>.
    /// </summary>
    /// <param name="pdfFilePath">The PDF's absolute path.</param>
    /// <param name="cancellationToken">Cancels the extraction.</param>
    /// <returns>The extracted text.</returns>
    Task<string> ExtractTextAsync(string pdfFilePath, CancellationToken cancellationToken = default);
}