// ============================================================================
// FILE:     src/ImageTextify.Core/Abstractions/IPdfTextExtractor.cs
// PURPOSE:  Contract for PDF text extraction. Embedded text is read
//           directly; scanned pages are rendered to images and OCR
//           runs on them. The original PDF is never modified.
// RELATION: Implemented in Phase 3. Consumed by the PDF extraction
//           pipeline.
// ============================================================================

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// Extracts text from a PDF stream.
/// </summary>
public interface IPdfTextExtractor
{
    /// <summary>
    /// Extracts text from a PDF.
    /// </summary>
    /// <param name="pdfStream">
    /// The PDF bytes. The caller owns the stream; the extractor must
    /// not close it. Null is rejected by the implementation.
    /// </param>
    /// <param name="options">
    /// Extraction options. Not null.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the extraction. The extractor honors the token and
    /// leaves the original PDF unchanged.
    /// </param>
    /// <returns>
    /// One entry per page, in page order, marking for each page whether
    /// the embedded text was used or OCR ran.
    /// </returns>
    Task<PdfTextResult> ExtractAsync(
        Stream pdfStream,
        PdfExtractionOptions options,
        CancellationToken cancellationToken);
}

/// <summary>
/// Options controlling PDF extraction.
/// </summary>
/// <param name="PageIndexes">
/// The zero-based indexes of the pages to extract, or null for all
/// pages. An empty list extracts no pages.
/// </param>
/// <param name="OcrScannedPages">
/// When true, render pages without a usable text layer and run OCR on
/// them. Default true.
/// </param>
public sealed record PdfExtractionOptions(
    IReadOnlyList<int>? PageIndexes = null,
    bool OcrScannedPages = true);

/// <summary>
/// The result of a PDF extraction.
/// </summary>
/// <param name="Pages">
/// One entry per extracted page, in page order. Never null.
/// </param>
public sealed record PdfTextResult(
    IReadOnlyList<PdfPageText> Pages);

/// <summary>
/// The extracted text of a single PDF page.
/// </summary>
/// <param name="PageIndex">
/// The zero-based page index in the original document.
/// </param>
/// <param name="Text">
/// The extracted text for this page. Never null; may be empty when the
/// page had no detectable text.
/// </param>
/// <param name="WasOcred">
/// True when the page had no usable text layer and OCR produced the
/// text. False when the embedded text layer was used directly.
/// </param>
public sealed record PdfPageText(
    int PageIndex,
    string Text,
    bool WasOcred);

/// <summary>
/// Thrown when a PDF is encrypted and cannot be read without
/// user-provided credentials.
/// </summary>
public sealed class PdfEncryptedException : Exception
{
    /// <summary>
    /// Creates the exception with a default message.
    /// </summary>
    public PdfEncryptedException()
        : base("The PDF is encrypted and requires user-provided credentials.")
    {
    }

    /// <summary>
    /// Creates the exception with a message.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public PdfEncryptedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates the exception with a message and an inner exception.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public PdfEncryptedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}