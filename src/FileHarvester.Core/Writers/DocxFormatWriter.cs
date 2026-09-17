// ============================================================================
// FILE:     src\FileHarvester.Core/Writers/DocxFormatWriter.cs
// PURPOSE:  Word .docx output. Content in monospace, plain background.
//           Renders into a MemoryStream first, then copies to the caller's
//           stream so the OpenXML package owns its stream exclusively.
// RELATION: Implements IFormatWriter. Registered in AddFileHarvester.
// ============================================================================

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FileHarvester.Core.Domain;

namespace FileHarvester.Core.Writers;

public sealed class DocxFormatWriter : IFormatWriter
{
    public string Extension => ".docx";
    public string DisplayName => "Word Document";
    public string MimeType =>
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public bool SupportsStreaming => false;

    public async Task WriteAsync(
        ComposedDocument document,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);

        if (document.Mode != OutputMode.SingleMerged)
        {
            throw new NotSupportedException(
                "DocxFormatWriter only renders SingleMerged documents.");
        }

        using var buffer = new MemoryStream();

        using (var package = WordprocessingDocument.Create(
            buffer, WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = package.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            for (var i = 0; i < document.Files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fc = document.Files[i];

                if (document.IncludeHeaders && i > 0)
                {
                    AppendPageBreak(body);
                }

                if (document.IncludeHeaders)
                {
                    AppendHeading(body, fc.File.AbsolutePath);
                    AppendMetadataLine(body, fc);
                }

                AppendContent(body, fc);

                if (i < document.Files.Count - 1 && !document.IncludeHeaders)
                {
                    body.AppendChild(new Paragraph());
                }
            }

            if (document.IncludeManifest)
            {
                AppendPageBreak(body);
                AppendManifest(body, document);
            }

            mainPart.Document.Save();
            package.Save();
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static void AppendHeading(Body body, string path)
    {
        body.AppendChild(new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = "Heading2" }),
            new Run(new Text(path) { Space = SpaceProcessingModeValues.Preserve })));
    }

    private static void AppendMetadataLine(Body body, FileContent fc)
    {
        var meta = $"{FormatBytes(fc.RawBytes.Length)} · SHA-256 {fc.Sha256} · {fc.Encoding.WebName}";
        body.AppendChild(new Paragraph(
            new Run(
                new RunProperties(new Italic(), new FontSize { Val = "18" }),
                new Text(meta) { Space = SpaceProcessingModeValues.Preserve })));
    }

    private static void AppendContent(Body body, FileContent fc)
    {
        if (fc.IsUnreadable)
        {
            body.AppendChild(new Paragraph(
                new Run(
                    new RunProperties(new Italic()),
                    new Text($"Unreadable: {fc.ReadError ?? "unknown"}"))));
            return;
        }

        if (fc.IsBinary)
        {
            body.AppendChild(new Paragraph(
                new Run(
                    new RunProperties(new Italic()),
                    new Text($"Binary file — {FormatBytes(fc.RawBytes.Length)}"))));
            return;
        }

        foreach (var line in SplitLines(fc.DecodedText))
        {
            // Plain paragraph — no shading, no background.
            body.AppendChild(new Paragraph(
                new ParagraphProperties(
                    new SpacingBetweenLines { Before = "0", After = "0" }),
                new Run(
                    new RunProperties(
                        new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas" },
                        new FontSize { Val = "18" }),
                    new Text(line.Length > 0 ? line : " ")
                    {
                        Space = SpaceProcessingModeValues.Preserve,
                    })));
        }
    }

    private static void AppendManifest(Body body, ComposedDocument document)
    {
        body.AppendChild(new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
            new Run(new Text("Manifest"))));

        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));

        var headerRow = new TableRow();
        headerRow.AppendChild(HeaderCell("Path"));
        headerRow.AppendChild(HeaderCell("Size"));
        headerRow.AppendChild(HeaderCell("SHA-256"));
        table.AppendChild(headerRow);

        foreach (var fc in document.Files)
        {
            var row = new TableRow();
            row.AppendChild(BodyCell(fc.File.AbsolutePath));
            row.AppendChild(BodyCell(FormatBytes(fc.RawBytes.Length)));
            row.AppendChild(BodyCell(fc.Sha256));
            table.AppendChild(row);
        }

        body.AppendChild(table);
    }

    private static TableCell HeaderCell(string text) =>
        new(
            new TableCellProperties(new Shading { Fill = "E5E7EB", Val = ShadingPatternValues.Clear }),
            new Paragraph(new Run(new RunProperties(new Bold()), new Text(text))));

    private static TableCell BodyCell(string text) =>
        new(new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve })));

    private static void AppendPageBreak(Body body)
    {
        body.AppendChild(new Paragraph(
            new Run(new Break { Type = BreakValues.Page })));
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
        return text.Split('\n').Select(l => l.TrimEnd('\r'));
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
