// ============================================================================
// filepath: src/ImageTextify.Platform/Conversion/MarkdownDocxConverter.cs
// layer:    Platform / Infrastructure
// module:   ImageTextify.Platform.Conversion
// since:    Phase 11 (Document/Image Conversion)
// purpose:  Converts Markdown <-> DOCX via DocSharp.Docx / DocSharp.Markdown.
//
// RELATIONSHIPS
//   Implements : n/a (per-format converter; called by ConversionService)
//   Depends on : DocSharp.Docx, DocSharp.Markdown, Markdig,
//                DocumentFormat.OpenXml
//   Used by    : ConversionService
//   See also   : Phase 11 Spike 2 (MD->DOCX 13868 bytes; DOCX->MD content round-trip)
//
// NOTES
//   - MD->DOCX uses Markdig to parse the markdown into a MarkdownDocument, then
//     MarkdownConverter.ToDocxBytes. The bytes are written to a temp path and
//     renamed into place (atomic).
//   - DOCX->MD uses WordprocessingDocument.Open + DocxToMarkdownConverter.Convert
//     into a StringWriter, then writes the string to a temp path and renames.
//   - HTML/TXT/RTF targets are not exposed in Phase 11; the proven matrix is MD<->DOCX.
// ============================================================================

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocSharp.Docx;
using DocSharp.Markdown;
using DocumentFormat.OpenXml.Packaging;
using ImageTextify.Core.Abstractions;
using Markdig;

namespace ImageTextify.Platform.Conversion;

/// <summary>
/// Markdown &lt;-&gt; DOCX converter over DocSharp. Atomic write.
/// </summary>
internal static class MarkdownDocxConverter
{
    /// <summary>
    /// Converts Markdown to DOCX. Atomic: no partial file on failure.
    /// </summary>
    /// <param name="inputPath">The .md file path. Not null.</param>
    /// <param name="outputPath">The .docx destination. Not null.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>The conversion result.</returns>
    public static Task<ConversionResult> MarkdownToDocxAsync(
        string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath)) return Task.FromResult(ConversionResult.Fail("Input file not found: " + inputPath));
        var tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var md = File.ReadAllText(inputPath);
            var parsed = Markdown.Parse(md);
            var bytes = new MarkdownConverter().ToDocxBytes(parsed, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, outputPath, overwrite: true);
            return Task.FromResult(ConversionResult.Ok(outputPath));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Task.FromResult(ConversionResult.Fail("Markdown to DOCX failed: " + ex.Message)); }
        finally { try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { } }
    }

    /// <summary>
    /// Converts DOCX to Markdown. Atomic: no partial file on failure.
    /// </summary>
    /// <param name="inputPath">The .docx file path. Not null.</param>
    /// <param name="outputPath">The .md destination. Not null.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>The conversion result.</returns>
    public static Task<ConversionResult> DocxToMarkdownAsync(
        string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath)) return Task.FromResult(ConversionResult.Fail("Input file not found: " + inputPath));
        var tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var doc = WordprocessingDocument.Open(inputPath, false);
            var sb = new StringBuilder();
            using (var sw = new StringWriter(sb))
            {
                new DocxToMarkdownConverter().Convert(doc, sw);
            }
            File.WriteAllText(tempPath, sb.ToString());
            File.Move(tempPath, outputPath, overwrite: true);
            return Task.FromResult(ConversionResult.Ok(outputPath));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Task.FromResult(ConversionResult.Fail("DOCX to Markdown failed: " + ex.Message)); }
        finally { try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { } }
    }
}
