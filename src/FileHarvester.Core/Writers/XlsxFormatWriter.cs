// ============================================================================
// FILE:     src\FileHarvester.Core/Writers/XlsxFormatWriter.cs
// PURPOSE:  Excel .xlsx output. Clean by default: one row per file with
//           just the content. The full column set with metadata appears
//           only when IncludeHeaders is true.
// RELATION: Implements IFormatWriter. Registered in AddFileHarvester.
// ============================================================================

using ClosedXML.Excel;
using FileHarvester.Core.Domain;

namespace FileHarvester.Core.Writers;

public sealed class XlsxFormatWriter : IFormatWriter
{
    private const int MaxCellChars = 32_000;

    public string Extension => ".xlsx";
    public string DisplayName => "Excel Workbook";
    public string MimeType =>
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public bool SupportsStreaming => false;

    public Task WriteAsync(
        ComposedDocument document,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);

        if (document.Mode != OutputMode.SingleMerged)
        {
            throw new NotSupportedException(
                "XlsxFormatWriter only renders SingleMerged documents.");
        }

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Bundle");

        if (document.IncludeHeaders)
        {
            WriteFullRows(sheet, document, cancellationToken);
        }
        else
        {
            WriteCleanRows(sheet, document, cancellationToken);
        }

        workbook.SaveAs(output);
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    // Clean: single column of content, one row per file.
    // ------------------------------------------------------------------

    private static void WriteCleanRows(
        IXLWorksheet sheet,
        ComposedDocument document,
        CancellationToken cancellationToken)
    {
        var row = 1;
        foreach (var fc in document.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            sheet.Cell(row, 1).Value = BuildContentCell(fc);
            sheet.Cell(row, 1).Style.Alignment.WrapText = true;
            sheet.Cell(row, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            row++;
        }

        sheet.Column(1).Width = 100;
        sheet.Rows().Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
    }

    // ------------------------------------------------------------------
    // Full: all metadata columns.
    // ------------------------------------------------------------------

    private static void WriteFullRows(
        IXLWorksheet sheet,
        ComposedDocument document,
        CancellationToken cancellationToken)
    {
        var headers = new[]
        {
            "#", "Path", "Size (bytes)", "SHA-256", "Encoding",
            "Is Binary", "Is Unreadable", "Content",
        };

        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Range(1, 1, 1, headers.Length).SetAutoFilter();

        var row = 2;
        foreach (var fc in document.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            sheet.Cell(row, 1).Value = row - 1;
            sheet.Cell(row, 2).Value = fc.File.AbsolutePath;
            sheet.Cell(row, 3).Value = fc.RawBytes.Length;
            sheet.Cell(row, 4).Value = fc.Sha256;
            sheet.Cell(row, 5).Value = fc.Encoding.WebName;
            sheet.Cell(row, 6).Value = fc.IsBinary;
            sheet.Cell(row, 7).Value = fc.IsUnreadable;
            sheet.Cell(row, 8).Value = BuildContentCell(fc);
            sheet.Cell(row, 8).Style.Alignment.WrapText = true;
            sheet.Cell(row, 8).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            row++;
        }

        sheet.Column(1).Width = 6;
        sheet.Column(2).Width = 60;
        sheet.Column(3).Width = 14;
        sheet.Column(4).Width = 66;
        sheet.Column(5).Width = 10;
        sheet.Column(6).Width = 10;
        sheet.Column(7).Width = 12;
        sheet.Column(8).Width = 80;
        sheet.Rows().Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
    }

    private static string BuildContentCell(FileContent fc)
    {
        if (fc.IsUnreadable)
        {
            return $"Unreadable: {fc.ReadError ?? "unknown"}";
        }

        if (fc.IsBinary)
        {
            var b64 = Convert.ToBase64String(fc.RawBytes);
            return b64.Length > MaxCellChars ? b64[..MaxCellChars] + "…[truncated]" : b64;
        }

        var text = fc.DecodedText;
        return text.Length > MaxCellChars ? text[..MaxCellChars] + "…[truncated]" : text;
    }
}
