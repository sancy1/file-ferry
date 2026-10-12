using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SchemaLence.Core.Connections;
using SchemaLence.Core.Providers;

namespace FileHarvester.App.Services;

public sealed class SchemaLenceExcelExporter
{
    private const int ExportBatchSize = 500;
    private const uint MaximumWorksheetRows = 1_048_576;
    private const int MaximumWorksheetColumns = 16_384;
    private const int MaximumCellTextLength = 32_767;

    public static async Task<long> ExportAsync(
        IDatabaseProvider provider,
        DatabaseConnectionProfile profile,
        string password,
        string namespaceName,
        string objectName,
        string destinationPath,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        using var document = SpreadsheetDocument.Create(
            destinationPath,
            SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = CreateStylesheet();
        stylesPart.Stylesheet.Save();
        var worksheetParts = new List<WorksheetPart>();
        OpenXmlWriter? writer = null;
        uint rowIndex = 1;
        uint dataRowsOnSheet = 0;
        long exportedRows = 0;
        IReadOnlyList<string>? headers = null;
        try
        {
            await provider.StreamRowsAsync(
                profile,
                password,
                namespaceName,
                objectName,
                ExportBatchSize,
                (columns, rows) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (headers is null)
                    {
                        if (columns.Count is < 1 or > MaximumWorksheetColumns)
                        {
                            throw new InvalidOperationException(
                                $"The selected object has {columns.Count:N0} columns; Excel worksheets support 1 to {MaximumWorksheetColumns:N0} columns.");
                        }
                        headers = columns;
                        writer = CreateWorksheetWriter(
                            workbookPart, worksheetParts, headers, out rowIndex);
                    }

                    foreach (var row in rows)
                    {
                        if (dataRowsOnSheet == MaximumWorksheetRows - 1)
                        {
                            CloseWorksheetWriter(writer!);
                            writer = CreateWorksheetWriter(
                                workbookPart, worksheetParts, headers, out rowIndex);
                            dataRowsOnSheet = 0;
                        }

                        ValidateCellLengths(row);
                        WriteRow(writer!, rowIndex++, row, isHeader: false);
                        dataRowsOnSheet++;
                        exportedRows++;
                    }
                    progress?.Report(exportedRows);
                    return Task.CompletedTask;
                },
                cancellationToken).ConfigureAwait(false);

            if (writer is null)
            {
                throw new InvalidOperationException(
                    "The selected object did not provide any columns to export.");
            }
            CloseWorksheetWriter(writer);
            writer = null;

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            for (var index = 0; index < worksheetParts.Count; index++)
            {
                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetParts[index]),
                    SheetId = (uint)(index + 1),
                    Name = index == 0 ? "Data" : $"Data {index + 1}",
                });
            }
            workbookPart.Workbook.Save();
            return exportedRows;
        }
        finally
        {
            writer?.Dispose();
        }
    }

    private static OpenXmlWriter CreateWorksheetWriter(
        WorkbookPart workbookPart,
        List<WorksheetPart> worksheetParts,
        IReadOnlyList<string> headers,
        out uint rowIndex)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        worksheetParts.Add(worksheetPart);
        var writer = OpenXmlWriter.Create(worksheetPart);
        writer.WriteStartElement(new Worksheet());
        writer.WriteStartElement(new SheetData());
        WriteRow(writer, 1, headers, isHeader: true);
        rowIndex = 2;
        return writer;
    }

    private static void CloseWorksheetWriter(OpenXmlWriter writer)
    {
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.Close();
    }

    private static void ValidateCellLengths(IReadOnlyList<string?> row)
    {
        for (var index = 0; index < row.Count; index++)
        {
            if (row[index]?.Length > MaximumCellTextLength)
            {
                throw new InvalidOperationException(
                    $"A value in column {index + 1} exceeds Excel's {MaximumCellTextLength:N0}-character cell limit.");
            }
        }
    }

    private static void WriteRow(
        OpenXmlWriter writer,
        uint rowIndex,
        IReadOnlyList<string?> values,
        bool isHeader)
    {
        writer.WriteStartElement(new Row { RowIndex = rowIndex });
        for (var index = 0; index < values.Count; index++)
        {
            var cell = new Cell
            {
                CellReference = ColumnName(index + 1) + rowIndex,
                StyleIndex = isHeader ? 1U : 0U,
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new Text
                {
                    Text = values[index] ?? string.Empty,
                    Space = SpaceProcessingModeValues.Preserve,
                }),
            };
            writer.WriteElement(cell);
        }
        writer.WriteEndElement();
    }

    private static string ColumnName(int column)
    {
        var name = string.Empty;
        while (column > 0)
        {
            column--;
            name = (char)('A' + column % 26) + name;
            column /= 26;
        }
        return name;
    }

    private static Stylesheet CreateStylesheet() =>
        new(
            new Fonts(
                new Font(),
                new Font(new Bold())),
            new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 })),
            new Borders(new Border()),
            new CellStyleFormats(new CellFormat()),
            new CellFormats(new CellFormat(), new CellFormat { FontId = 1, ApplyFont = true }),
            new CellStyles(new CellStyle
            {
                Name = "Normal",
                FormatId = 0,
                BuiltinId = 0,
            }));
}
