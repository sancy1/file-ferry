// ============================================================================
// filepath: src/DeploymentVault.Platform/KeyForge/Documents/KeyForgeDocumentReader.cs
// layer:    Platform / Infrastructure
// module:   DeploymentVault.Platform.KeyForge.Documents
// since:    Phase A (KeyForge)
// purpose:  Reads an imported document (.txt, .md, .env, .json, .yaml, .docx,
//           .pdf, .xlsx) into plain text for the KeyForge extractor. KeyForge
//           forges keys in text form; it does not write back into the
//           original container.
// RELATIONSHIPS
//   Implements : IDocumentTextReader (DeploymentVault.Core.KeyForge.Abstractions)
//   Depends on : DocumentFormat.OpenXml (.docx), PdfPig (.pdf), ClosedXML (.xlsx)
//   Used by    : DeploymentVaultServiceCollectionExtensions, KeyForgeViewModel
//   See also   : EnvFileReader (same OpenXml .docx pattern), PdfPigTextExtractor
// NOTES
//   - Plain-text formats (.txt/.md/.env/.json/.yaml/.yml) are read as UTF-8.
//   - .docx paragraphs are joined with newlines (Body.InnerText loses breaks).
//   - .pdf uses PdfPig's ContentOrderTextExtractor per page.
//   - .xlsx iterates every sheet/row/cell and appends cell text.
//   - Unsupported extensions throw ArgumentException; the VM catches and
//     shows a user-facing message.
// ============================================================================

using System.Text;
using ClosedXML.Excel;
using DeploymentVault.Core.KeyForge.Abstractions;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace DeploymentVault.Platform.KeyForge.Documents;

/// <summary>
/// Reads an imported document into plain text.
/// </summary>
public sealed class KeyForgeDocumentReader : IDocumentTextReader
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".env", ".json", ".yaml", ".yml",
    };

    /// <inheritdoc/>
    public async Task<string> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var extension = Path.GetExtension(path);

        if (TextExtensions.Contains(extension))
        {
            return await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractDocxText(path);
        }

        if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return await Task.Run(() => ExtractPdfText(path, cancellationToken), cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return await Task.Run(() => ExtractXlsxText(path), cancellationToken).ConfigureAwait(false);
        }

        throw new ArgumentException($"Unsupported file type: {extension}", nameof(path));
    }

    private static string ExtractDocxText(string path)
    {
        using var document = WordprocessingDocument.Open(path, isEditable: false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null) return string.Empty;

        var sb = new StringBuilder();
        foreach (var paragraph in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
        {
            var text = paragraph.InnerText;
            if (text.Length > 0) sb.AppendLine(text);
        }
        return sb.ToString();
    }

    private static string ExtractPdfText(string path, CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(path);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            sb.AppendLine(ContentOrderTextExtractor.GetText(page));
        }
        return sb.ToString();
    }

    private static string ExtractXlsxText(string path)
    {
        using var workbook = new XLWorkbook(path);
        var sb = new StringBuilder();
        foreach (var sheet in workbook.Worksheets)
        {
            foreach (var row in sheet.RowsUsed())
            {
                foreach (var cell in row.CellsUsed())
                {
                    var text = cell.GetString();
                    if (!string.IsNullOrEmpty(text)) sb.AppendLine(text);
                }
            }
        }
        return sb.ToString();
    }
}
