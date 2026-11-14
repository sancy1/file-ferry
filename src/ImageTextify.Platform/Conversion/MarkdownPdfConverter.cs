// ============================================================================
// filepath: src/ImageTextify.Platform/Conversion/MarkdownPdfConverter.cs
// layer:    Platform / Infrastructure
// module:   ImageTextify.Platform.Conversion
// since:    Phase 11 (Document/Image Conversion)
// purpose:  Converts Markdown to PDF via TinyPdf. One-way; the free stack has
//           no PDF->Markdown converter (finding F-031 adjacent).
//
// RELATIONSHIPS
//   Implements : n/a (per-format converter; called by ConversionService)
//   Depends on : TinyPdf
//   Used by    : ConversionService
//   See also   : Phase 11 Spike 2 (MD->PDF 2541 bytes, %PDF magic)
//
// NOTES
//   - TinyPdfCreate.Markdown returns byte[]; write to a temp path and rename
//     atomically. No partial PDF on failure.
// ============================================================================

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;
using TinyPdf;

namespace ImageTextify.Platform.Conversion;

/// <summary>
/// Markdown to PDF converter over TinyPdf. One-way.
/// </summary>
internal static class MarkdownPdfConverter
{
    /// <summary>
    /// Converts a Markdown file to PDF. Atomic: no partial file on failure.
    /// </summary>
    /// <param name="inputPath">The .md file path. Not null.</param>
    /// <param name="outputPath">The .pdf destination. Not null.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>The conversion result.</returns>
    public static Task<ConversionResult> ConvertAsync(
        string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath)) return Task.FromResult(ConversionResult.Fail("Input file not found: " + inputPath));
        var tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var md = File.ReadAllText(inputPath);
            var bytes = TinyPdfCreate.Markdown(md, new TinyPdfCreate.MarkdownOptions(Compress: false));
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, outputPath, overwrite: true);
            return Task.FromResult(ConversionResult.Ok(outputPath));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Task.FromResult(ConversionResult.Fail("Markdown to PDF failed: " + ex.Message)); }
        finally { try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { } }
    }
}
