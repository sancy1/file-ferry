// ============================================================================
// FILE:     src\FileHarvester.Core/Writers/PdfFormatWriter.cs
// PURPOSE:  PDF output. Clean by default: content in monospace on a plain
//           page. Cover page, per-file headers, and manifest are opt-in.
// RELATION: Implements IFormatWriter. Registered in AddFileHarvester.
// ============================================================================

using System.Globalization;
using FileHarvester.Core.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FileHarvester.Core.Writers;

public sealed class PdfFormatWriter : IFormatWriter
{
    private const int MaxContentChars = 1_000_000;

    static PdfFormatWriter()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public string Extension => ".pdf";
    public string DisplayName => "PDF";
    public string MimeType => "application/pdf";
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
                "PdfFormatWriter only renders SingleMerged documents.");
        }

        var pdf = Document.Create(container =>
        {
            if (document.IncludeHeaders)
            {
                ComposeCover(container, document);
            }

            ComposeFiles(container, document, cancellationToken);

            if (document.IncludeManifest)
            {
                ComposeManifest(container, document);
            }
        });

        pdf.GeneratePdf(output);
        return Task.CompletedTask;
    }

    private static void ComposeCover(IDocumentContainer container, ComposedDocument document)
    {
        var totalBytes = document.Files.Sum(f => (long)f.RawBytes.Length);
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(60);
            page.DefaultTextStyle(x => x.FontSize(12));

            page.Content().AlignMiddle().Column(col =>
            {
                col.Item().Text("File-Ferry Bundle").FontSize(28).Bold();
                col.Item().PaddingTop(20)
                    .Text($"{document.Files.Count} file(s)").FontSize(14);
                col.Item().Text($"{FormatBytes(totalBytes)} total").FontSize(14);
                col.Item().PaddingTop(20)
                    .Text($"Generated {now} UTC")
                    .FontSize(10).FontColor(Colors.Grey.Darken1);
            });
        });
    }

    private static void ComposeFiles(
        IDocumentContainer container,
        ComposedDocument document,
        CancellationToken cancellationToken)
    {
        foreach (var fc in document.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Content().Column(col =>
                {
                    if (document.IncludeHeaders)
                    {
                        col.Item().Text(fc.File.AbsolutePath).FontSize(14).Bold();

                        var meta = $"{FormatBytes(fc.RawBytes.Length)} · " +
                                   $"SHA-256 {fc.Sha256} · {fc.Encoding.WebName}";
                        col.Item().PaddingTop(4)
                            .Text(meta).FontSize(8).FontColor(Colors.Grey.Darken1);

                        col.Item().PaddingTop(12);
                    }

                    if (fc.IsUnreadable)
                    {
                        col.Item().Text($"Unreadable: {fc.ReadError ?? "unknown"}")
                            .Italic().FontColor(Colors.Orange.Darken2);
                    }
                    else if (fc.IsBinary)
                    {
                        col.Item().Text($"Binary file — {FormatBytes(fc.RawBytes.Length)}")
                            .Italic().FontColor(Colors.Orange.Darken2);
                    }
                    else
                    {
                        var content = fc.DecodedText;
                        var truncated = false;

                        if (content.Length > MaxContentChars)
                        {
                            content = content[..MaxContentChars];
                            truncated = true;
                        }

                        if (document.IncludeHeaders)
                        {
                            col.Item()
                                .Background(Colors.Grey.Lighten4)
                                .Padding(8)
                                .Text(content)
                                .FontFamily(Fonts.Consolas)
                                .FontSize(8);
                        }
                        else
                        {
                            col.Item()
                                .Text(content)
                                .FontFamily(Fonts.Consolas)
                                .FontSize(9);
                        }

                        if (truncated)
                        {
                            col.Item().PaddingTop(8)
                                .Text($"[Content truncated at {MaxContentChars:N0} characters]")
                                .Italic().FontSize(8).FontColor(Colors.Orange.Darken2);
                        }
                    }
                });
            });
        }
    }

    private static void ComposeManifest(IDocumentContainer container, ComposedDocument document)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(x => x.FontSize(9));

            page.Content().Column(col =>
            {
                col.Item().Text("Manifest").FontSize(16).Bold();
                col.Item().PaddingTop(12);

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(4);
                        cols.RelativeColumn(1.2f);
                        cols.RelativeColumn(2.4f);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Grey.Lighten2).Padding(4)
                            .Text("Path").Bold();
                        header.Cell().Background(Colors.Grey.Lighten2).Padding(4)
                            .Text("Size").Bold();
                        header.Cell().Background(Colors.Grey.Lighten2).Padding(4)
                            .Text("SHA-256").Bold();
                    });

                    foreach (var fc in document.Files)
                    {
                        table.Cell().Padding(4)
                            .Text(fc.File.AbsolutePath).FontFamily(Fonts.Consolas).FontSize(7);
                        table.Cell().Padding(4)
                            .Text(FormatBytes(fc.RawBytes.Length)).FontSize(7);
                        table.Cell().Padding(4)
                            .Text(fc.Sha256).FontFamily(Fonts.Consolas).FontSize(7);
                    }
                });
            });
        });
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
