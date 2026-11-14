// ============================================================================
// filepath: src/ImageTextify.Platform/Conversion/TextDocumentConverter.cs
// layer:    Platform / Infrastructure
// module:   ImageTextify.Platform.Conversion
// since:    Phase 11 (Document/Image Conversion)
// purpose:  Converts plain-text and text-derived documents: TXT <-> MD,
//           TXT -> DOCX/PDF, MD -> TXT, DOCX -> TXT/PDF, CSV/JSON -> TXT.
//           Plain text is valid Markdown, so TXT sources ride the existing
//           Markdig/DocSharp/TinyPdf pipeline. DOCX sources chain through
//           the proven DOCX -> Markdown step first.
//
// RELATIONSHIPS
//   Implements : n/a (per-format converter; called by ConversionService)
//   Depends on : DocSharp.Docx, DocSharp.Markdown, Markdig, TinyPdf,
//                DocumentFormat.OpenXml
//   Used by    : ConversionService
//   See also   : MarkdownDocxConverter.cs, MarkdownPdfConverter.cs
//
// NOTES
//   - Every method writes to a temp path and renames atomically; no partial
//     file is left behind on failure or cancellation.
//   - PDF as a SOURCE remains unsupported (no free reader; finding F-032).
//   - XLSX as a source remains limited to the F-030 matrix.
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
using TinyPdf;

namespace ImageTextify.Platform.Conversion;

/// <summary>
/// Text-document converter over the same free stack as the Markdown converters.
/// </summary>
internal static class TextDocumentConverter
{
    /// <summary>
    /// Copies a text file to a new path atomically. Used for pairs where the
    /// payload is already valid in the target format (TXT &lt;-&gt; MD,
    /// CSV/JSON &lt;-&gt; TXT).
    /// </summary>
    /// <param name="inputPath">The source file path. Not null.</param>
    /// <param name="outputPath">The destination path. Not null.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>The conversion result.</returns>
    public static Task<ConversionResult> CopyAsync(
        string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath)) return Task.FromResult(ConversionResult.Fail("Input file not found: " + inputPath));
        var tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(inputPath, tempPath, overwrite: true);
            File.Move(tempPath, outputPath, overwrite: true);
            return Task.FromResult(ConversionResult.Ok(outputPath));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Task.FromResult(ConversionResult.Fail("Text copy failed: " + ex.Message)); }
        finally { try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { } }
    }

    /// <summary>
    /// Converts a plain-text file to DOCX. The text is parsed as Markdown
    /// (plain text is valid Markdown). Atomic.
    /// </summary>
    /// <param name="inputPath">The .txt file path. Not null.</param>
    /// <param name="outputPath">The .docx destination. Not null.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>The conversion result.</returns>
    public static Task<ConversionResult> TextToDocxAsync(
        string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath)) return Task.FromResult(ConversionResult.Fail("Input file not found: " + inputPath));
        var tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = File.ReadAllText(inputPath);
            var parsed = Markdown.Parse(text);
            var bytes = new MarkdownConverter().ToDocxBytes(parsed, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, outputPath, overwrite: true);
            return Task.FromResult(ConversionResult.Ok(outputPath));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Task.FromResult(ConversionResult.Fail("Text to DOCX failed: " + ex.Message)); }
        finally { try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { } }
    }

    /// <summary>
    /// Converts a plain-text file to PDF via TinyPdf (text parsed as
    /// Markdown). Atomic.
    /// </summary>
    /// <param name="inputPath">The .txt file path. Not null.</param>
    /// <param name="outputPath">The .pdf destination. Not null.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>The conversion result.</returns>
    public static Task<ConversionResult> TextToPdfAsync(
        string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath)) return Task.FromResult(ConversionResult.Fail("Input file not found: " + inputPath));
        var tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = File.ReadAllText(inputPath);
            var bytes = TinyPdfCreate.Markdown(text, new TinyPdfCreate.MarkdownOptions(Compress: false));
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, outputPath, overwrite: true);
            return Task.FromResult(ConversionResult.Ok(outputPath));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Task.FromResult(ConversionResult.Fail("Text to PDF failed: " + ex.Message)); }
        finally { try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { } }
    }

    /// <summary>
    /// Converts a plain-text/Markdown file to CSV: one record per line, a
    /// single quoted-when-needed field. Atomic.
    /// </summary>
    /// <param name="inputPath">The .txt/.md file path. Not null.</param>
    /// <param name="outputPath">The .csv destination. Not null.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>The conversion result.</returns>
    public static Task<ConversionResult> TextToCsvAsync(
        string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath)) return Task.FromResult(ConversionResult.Fail("Input file not found: " + inputPath));
        var tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = File.ReadAllText(inputPath);
            var sb = new StringBuilder();
            foreach (var line in text.Split('\n'))
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.Append(CsvField(line.TrimEnd('\r'))).Append('\n');
            }
            File.WriteAllText(tempPath, sb.ToString(), Encoding.UTF8);
            File.Move(tempPath, outputPath, overwrite: true);
            return Task.FromResult(ConversionResult.Ok(outputPath));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Task.FromResult(ConversionResult.Fail("Text to CSV failed: " + ex.Message)); }
        finally { try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { } }
    }

    /// <summary>
    /// Converts a plain-text/Markdown file to JSON: an array of line
    /// strings. Atomic.
    /// </summary>
    /// <param name="inputPath">The .txt/.md file path. Not null.</param>
    /// <param name="outputPath">The .json destination. Not null.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>The conversion result.</returns>
    public static Task<ConversionResult> TextToJsonAsync(
        string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath)) return Task.FromResult(ConversionResult.Fail("Input file not found: " + inputPath));
        var tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = File.ReadAllText(inputPath);
            var lines = text.Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .Where(l => l.Length > 0)
                .ToArray();
            var json = System.Text.Json.JsonSerializer.Serialize(lines);
            File.WriteAllText(tempPath, json, Encoding.UTF8);
            File.Move(tempPath, outputPath, overwrite: true);
            return Task.FromResult(ConversionResult.Ok(outputPath));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Task.FromResult(ConversionResult.Fail("Text to JSON failed: " + ex.Message)); }
        finally { try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { } }
    }

    private static string CsvField(string value)
    {
        if (value.IndexOf(',') < 0 && value.IndexOf('"') < 0 && value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>
    /// Converts a DOCX file to plain text by chaining through Markdown
    /// (DOCX -> MD -> TXT). Atomic.
    /// </summary>
    /// <param name="inputPath">The .docx file path. Not null.</param>
    /// <param name="outputPath">The .txt destination. Not null.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>The conversion result.</returns>
    public static async Task<ConversionResult> DocxToTextAsync(
        string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        var md = await DocxToMarkdownStringAsync(inputPath, cancellationToken).ConfigureAwait(false);
        if (md is null) return ConversionResult.Fail("DOCX to text failed: could not read the document.");
        var tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(tempPath, md, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, outputPath, overwrite: true);
            return ConversionResult.Ok(outputPath);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return ConversionResult.Fail("DOCX to text failed: " + ex.Message); }
        finally { try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { } }
    }

    /// <summary>
    /// Converts a DOCX file to PDF by chaining DOCX -> Markdown -> PDF.
    /// Atomic.
    /// </summary>
    /// <param name="inputPath">The .docx file path. Not null.</param>
    /// <param name="outputPath">The .pdf destination. Not null.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>The conversion result.</returns>
    public static async Task<ConversionResult> DocxToPdfAsync(
        string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        var md = await DocxToMarkdownStringAsync(inputPath, cancellationToken).ConfigureAwait(false);
        if (md is null) return ConversionResult.Fail("DOCX to PDF failed: could not read the document.");
        var tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = TinyPdfCreate.Markdown(md, new TinyPdfCreate.MarkdownOptions(Compress: false));
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, outputPath, overwrite: true);
            return ConversionResult.Ok(outputPath);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return ConversionResult.Fail("DOCX to PDF failed: " + ex.Message); }
        finally { try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { } }
    }

    /// <summary>
    /// Reads a DOCX and returns its Markdown text, or null when the file
    /// cannot be opened. Same converter MarkdownDocxConverter uses.
    /// </summary>
    private static async Task<string?> DocxToMarkdownStringAsync(string inputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath)) return null;
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var doc = WordprocessingDocument.Open(inputPath, false);
            var sb = new StringBuilder();
            using (var sw = new StringWriter(sb))
            {
                new DocxToMarkdownConverter().Convert(doc, sw);
            }
            return sb.ToString();
        }, cancellationToken).ConfigureAwait(false);
    }
}
