// filepath: src/ImageTextify.Platform/Extraction/PdfPageRasterizer.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Extraction
// since:    Phase 2.6-PDF-2
// purpose:  Renders a PDF page to PNG bytes with PdfPig.Rendering.Skia, so
//           a scanned PDF (one with no text layer) can be fed to the image
//           OCR pipeline.
// RELATION: Used by the PDF extraction path when the text layer is empty.
// NOTES:    - GetPageAsPng returns a stream whose Position may not be zero;
//             this class resets it before reading, per the package issue
//             tracker.
//           - The renderer requires AddSkiaPageFactory once per document and
//             the SkiaRenderingParsingOptions parsing mode at Open time.
//           - The caller owns the input stream; this class does not dispose
//             it. The PdfPig document is disposed.
// ============================================================================

using System;
using System.IO;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Rendering.Skia;

namespace ImageTextify.Platform.Extraction;

/// <summary>
/// Renders a PDF page to PNG bytes for OCR.
/// </summary>
public static class PdfPageRasterizer
{
    /// <summary>
    /// Renders one page to PNG bytes.
    /// </summary>
    /// <param name="pdfStream">The PDF bytes. The caller owns the stream.</param>
    /// <param name="pageNumber">One-based page number.</param>
    /// <param name="scale">Render scale; 2.0 doubles the page resolution.</param>
    /// <returns>The PNG bytes of the rendered page.</returns>
    public static byte[] RenderPageToPng(Stream pdfStream, int pageNumber, float scale)
    {
        ArgumentNullException.ThrowIfNull(pdfStream);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        using var document = PdfDocument.Open(pdfStream, SkiaRenderingParsingOptions.Instance);
        document.AddSkiaPageFactory();
        using var png = document.GetPageAsPng(pageNumber, scale);
        png.Position = 0;
        return png.ToArray();
    }
}
