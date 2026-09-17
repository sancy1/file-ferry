// ============================================================================
// FILE:     src\FileHarvester.Core/Writers/MarkdownFormatWriter.cs
// PURPOSE:  Markdown output. Clean by default: only the file contents.
//           Titles, per-file headings, code fences, TOC, and manifest are
//           all opt-in via document flags.
// RELATION: Implements IFormatWriter. Registered in AddFileHarvester.
// ============================================================================

using System.Text;
using FileHarvester.Core.Domain;

namespace FileHarvester.Core.Writers;

public sealed class MarkdownFormatWriter : IFormatWriter
{
    public string Extension => ".md";
    public string DisplayName => "Markdown";
    public string MimeType => "text/markdown";
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
                "MarkdownFormatWriter only renders SingleMerged documents.");
        }

        var sb = new StringBuilder();

        // Optional TOC.
        if (document.IncludeTableOfContents)
        {
            sb.Append("## Contents");
            sb.Append('\n');
            sb.Append('\n');

            foreach (var fc in document.Files)
            {
                sb.Append("- `");
                sb.Append(fc.File.AbsolutePath);
                sb.Append("` (");
                sb.Append(FormatBytes(fc.RawBytes.Length));
                sb.Append(')');
                sb.Append('\n');
            }

            sb.Append('\n');
            sb.Append("---");
            sb.Append('\n');
            sb.Append('\n');
        }

        // Per-file sections.
        for (var i = 0; i < document.Files.Count; i++)
        {
            var fc = document.Files[i];

            // Optional header.
            if (document.IncludeHeaders)
            {
                sb.Append("## `");
                sb.Append(fc.File.AbsolutePath);
                sb.Append('`');
                sb.Append('\n');
                sb.Append('\n');
            }

            if (fc.IsUnreadable)
            {
                sb.Append("> **Unreadable:** ");
                sb.Append(fc.ReadError ?? "unknown");
                sb.Append('\n');
            }
            else if (fc.IsBinary)
            {
                sb.Append("> **Binary file** (");
                sb.Append(FormatBytes(fc.RawBytes.Length));
                sb.Append(", SHA-256 `");
                sb.Append(fc.Sha256);
                sb.Append("`)");
                sb.Append('\n');
            }
            else if (document.IncludeCodeFences)
            {
                var language = LanguageForExtension(fc.File.AbsolutePath);
                sb.Append("```");
                sb.Append(language);
                sb.Append('\n');
                sb.Append(fc.DecodedText);
                if (!fc.DecodedText.EndsWith('\n'))
                {
                    sb.Append('\n');
                }
                sb.Append("```");
                sb.Append('\n');
            }
            else
            {
                sb.Append(fc.DecodedText);
                if (!fc.DecodedText.EndsWith('\n'))
                {
                    sb.Append('\n');
                }
            }

            // Separation between entries.
            if (i < document.Files.Count - 1)
            {
                sb.Append('\n');
                if (document.TwoBlankLines)
                {
                    sb.Append('\n');
                }
            }
        }

        // Optional manifest.
        if (document.IncludeManifest)
        {
            sb.Append('\n');
            sb.Append("---");
            sb.Append('\n');
            sb.Append('\n');
            sb.Append("## Manifest");
            sb.Append('\n');
            sb.Append('\n');
            sb.Append("| Path | Size | SHA-256 |");
            sb.Append('\n');
            sb.Append("|---|---|---|");
            sb.Append('\n');

            foreach (var fc in document.Files)
            {
                sb.Append("| `");
                sb.Append(fc.File.AbsolutePath);
                sb.Append("` | ");
                sb.Append(FormatBytes(fc.RawBytes.Length));
                sb.Append(" | `");
                sb.Append(fc.Sha256);
                sb.Append("` |");
                sb.Append('\n');
            }
        }

        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes(sb.ToString());

        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    private static string LanguageForExtension(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "csharp",
            ".csproj" => "xml",
            ".slnx" => "xml",
            ".json" => "json",
            ".xml" => "xml",
            ".yml" => "yaml",
            ".yaml" => "yaml",
            ".md" => "markdown",
            ".html" => "html",
            ".htm" => "html",
            ".css" => "css",
            ".js" => "javascript",
            ".ts" => "typescript",
            ".py" => "python",
            ".go" => "go",
            ".rs" => "rust",
            ".sh" => "bash",
            ".ps1" => "powershell",
            ".sql" => "sql",
            _ => "text",
        };
    }
}
