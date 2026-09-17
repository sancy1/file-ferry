// ============================================================================
// FILE:     src\FileHarvester.Core/Writers/HtmlFormatWriter.cs
// PURPOSE:  HTML output. Clean by default: minimal <html><body><pre>content</pre>.
//           The styled document, TOC, per-file details blocks, and manifest
//           appear only when the corresponding flags are set.
// RELATION: Implements IFormatWriter. Registered in AddFileHarvester.
// ============================================================================

using System.Globalization;
using System.Net;
using System.Text;
using FileHarvester.Core.Domain;

namespace FileHarvester.Core.Writers;

public sealed class HtmlFormatWriter : IFormatWriter
{
    public string Extension => ".html";
    public string DisplayName => "HTML";
    public string MimeType => "text/html";
    public bool SupportsStreaming => true;

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
                "HtmlFormatWriter only renders SingleMerged documents.");
        }

        var sb = new StringBuilder();

        sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n");
        sb.Append("<meta charset=\"utf-8\">\n");
        sb.Append("<title>File-Ferry</title>\n");

        if (document.IncludeHeaders)
        {
            AppendStyles(sb);
        }

        sb.Append("</head>\n<body>\n");

        if (document.IncludeHeaders)
        {
            AppendStyledHeader(sb, document);
        }

        if (document.IncludeTableOfContents)
        {
            AppendToc(sb, document);
        }

        if (document.IncludeHeaders)
        {
            foreach (var fc in document.Files)
            {
                AppendFileDetails(sb, fc);
            }
        }
        else
        {
            // Clean: concatenate all decoded text into a single pre block.
            sb.Append("<pre>");
            for (var i = 0; i < document.Files.Count; i++)
            {
                var fc = document.Files[i];

                if (fc.IsUnreadable)
                {
                    sb.Append(WebUtility.HtmlEncode($"[unreadable: {fc.ReadError ?? "unknown"}]"));
                }
                else if (fc.IsBinary)
                {
                    sb.Append(WebUtility.HtmlEncode($"[binary: {fc.RawBytes.Length} bytes]"));
                }
                else
                {
                    sb.Append(WebUtility.HtmlEncode(fc.DecodedText));
                }

                if (i < document.Files.Count - 1)
                {
                    sb.Append('\n');
                    if (document.TwoBlankLines) sb.Append('\n');
                }
            }
            sb.Append("</pre>\n");
        }

        if (document.IncludeManifest)
        {
            AppendManifest(sb, document);
        }

        sb.Append("</body>\n</html>\n");

        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes(sb.ToString());

        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static void AppendStyles(StringBuilder sb)
    {
        sb.Append("<style>\n");
        sb.Append(":root { color-scheme: light dark; }\n");
        sb.Append("body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 1100px; margin: 2em auto; padding: 0 1em; line-height: 1.5; }\n");
        sb.Append("pre { background: #8881; padding: 1em; border-radius: 4px; overflow-x: auto; font-size: 0.85em; }\n");
        sb.Append("code { font-family: Consolas, Menlo, monospace; }\n");
        sb.Append("details { margin: 1em 0; border: 1px solid #8884; border-radius: 6px; padding: 0.5em 1em; }\n");
        sb.Append("summary { cursor: pointer; font-family: Consolas, Menlo, monospace; font-size: 0.95em; }\n");
        sb.Append("table { border-collapse: collapse; width: 100%; margin-top: 1em; font-size: 0.85em; }\n");
        sb.Append("th, td { border: 1px solid #8884; padding: 0.4em 0.6em; text-align: left; }\n");
        sb.Append("</style>\n");
    }

    private static void AppendStyledHeader(StringBuilder sb, ComposedDocument document)
    {
        var totalBytes = document.Files.Sum(f => (long)f.RawBytes.Length);
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        sb.Append("<h1>File-Ferry Bundle</h1>\n");
        sb.Append("<div class=\"meta\">");
        sb.Append(document.Files.Count.ToString(CultureInfo.InvariantCulture));
        sb.Append(" file(s) · ");
        sb.Append(FormatBytes(totalBytes));
        sb.Append(" total · generated ");
        sb.Append(now);
        sb.Append(" UTC</div>\n");
    }

    private static void AppendToc(StringBuilder sb, ComposedDocument document)
    {
        sb.Append("<h2>Contents</h2>\n<ul>\n");
        foreach (var fc in document.Files)
        {
            sb.Append("<li><code>");
            sb.Append(WebUtility.HtmlEncode(fc.File.AbsolutePath));
            sb.Append("</code></li>\n");
        }
        sb.Append("</ul>\n");
    }

    private static void AppendFileDetails(StringBuilder sb, FileContent fc)
    {
        sb.Append("<details>\n<summary><code>");
        sb.Append(WebUtility.HtmlEncode(fc.File.AbsolutePath));
        sb.Append("</code></summary>\n");

        if (fc.IsUnreadable)
        {
            sb.Append("<div>Unreadable: ");
            sb.Append(WebUtility.HtmlEncode(fc.ReadError ?? "unknown"));
            sb.Append("</div>\n");
        }
        else if (fc.IsBinary)
        {
            sb.Append("<div>Binary file — ");
            sb.Append(FormatBytes(fc.RawBytes.Length));
            sb.Append("</div>\n");
        }
        else
        {
            sb.Append("<pre><code>");
            sb.Append(WebUtility.HtmlEncode(fc.DecodedText));
            sb.Append("</code></pre>\n");
        }

        sb.Append("</details>\n");
    }

    private static void AppendManifest(StringBuilder sb, ComposedDocument document)
    {
        sb.Append("<h2>Manifest</h2>\n<table>\n<thead><tr><th>Path</th><th>Size</th><th>SHA-256</th></tr></thead>\n<tbody>\n");
        foreach (var fc in document.Files)
        {
            sb.Append("<tr><td>");
            sb.Append(WebUtility.HtmlEncode(fc.File.AbsolutePath));
            sb.Append("</td><td>");
            sb.Append(FormatBytes(fc.RawBytes.Length));
            sb.Append("</td><td><code>");
            sb.Append(WebUtility.HtmlEncode(fc.Sha256));
            sb.Append("</code></td></tr>\n");
        }
        sb.Append("</tbody>\n</table>\n");
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
