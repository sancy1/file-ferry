// ============================================================================
// FILE:     src\FileHarvester.Core/Writers/CsvFormatWriter.cs
// PURPOSE:  CSV output (RFC 4180).
//           Clean mode (default): one row per source line, so content is
//           visible in Excel and every other tool without special settings.
//           Full mode (IncludeHeaders = true): one row per file with
//           metadata columns.
// RELATION: Implements IFormatWriter. Registered in AddFileHarvester.
// ============================================================================

using System.Buffers;
using System.Globalization;
using System.Text;
using FileHarvester.Core.Domain;

namespace FileHarvester.Core.Writers;

public sealed class CsvFormatWriter : IFormatWriter
{
    private const int MaxCellChars = 32_000;

    private static readonly SearchValues<char> CharsToQuote =
        SearchValues.Create(new[] { ',', '"', '\r', '\n' });

    private static readonly string[] FullHeaders =
    {
        "#", "Path", "Size (bytes)", "SHA-256",
        "Encoding", "Is Binary", "Is Unreadable", "Content",
    };

    private static readonly string[] CleanSingleHeaders = { "Line" };
    private static readonly string[] CleanMultiHeaders  = { "File", "Line" };

    public string Extension => ".csv";
    public string DisplayName => "Comma-Separated";
    public string MimeType => "text/csv";
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
                "CsvFormatWriter only renders SingleMerged documents.");
        }

        var sb = new StringBuilder();

        if (document.IncludeHeaders)
        {
            WriteFullRows(sb, document);
        }
        else
        {
            WriteCleanRows(sb, document);
        }

        // UTF-8 with BOM so Excel opens the file correctly.
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            .GetBytes(sb.ToString());

        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // Clean mode: one row per source line.
    // ------------------------------------------------------------------

    private static void WriteCleanRows(StringBuilder sb, ComposedDocument document)
    {
        var multipleFiles = document.Files.Count > 1;

        // Header row.
        var headers = multipleFiles ? CleanMultiHeaders : CleanSingleHeaders;
        sb.Append(string.Join(",", headers.Select(Escape)));
        sb.Append("\r\n");

        foreach (var fc in document.Files)
        {
            var sourcePath = fc.File.AbsolutePath;

            // Binary and unreadable files get one summary row.
            if (fc.IsUnreadable)
            {
                AppendRow(sb, multipleFiles, sourcePath,
                    $"[unreadable: {fc.ReadError ?? "unknown"}]");
                continue;
            }

            if (fc.IsBinary)
            {
                AppendRow(sb, multipleFiles, sourcePath,
                    $"[binary: {fc.RawBytes.Length} bytes]");
                continue;
            }

            // Text: split by lines. Preserve empty lines.
            var lines = SplitLines(fc.DecodedText);

            if (lines.Count == 0)
            {
                AppendRow(sb, multipleFiles, sourcePath, string.Empty);
                continue;
            }

            foreach (var line in lines)
            {
                AppendRow(sb, multipleFiles, sourcePath, line);
            }
        }
    }

    private static void AppendRow(
        StringBuilder sb,
        bool multipleFiles,
        string sourcePath,
        string lineValue)
    {
        if (multipleFiles)
        {
            sb.Append(Escape(sourcePath));
            sb.Append(',');
        }

        sb.Append(Escape(Truncate(lineValue, MaxCellChars)));
        sb.Append("\r\n");
    }

    /// <summary>
    /// Splits text into lines without stripping carriage returns. Empty
    /// lines are preserved as empty strings.
    /// </summary>
    private static IReadOnlyList<string> SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '\n')
            {
                var end = i;
                if (end > start && text[end - 1] == '\r')
                {
                    end--;
                }

                result.Add(text.Substring(start, end - start));
                start = i + 1;
            }
        }

        // Trailing content after the last newline.
        if (start < text.Length)
        {
            result.Add(text.Substring(start));
        }

        return result;
    }

    private static string Truncate(string value, int max) =>
        value.Length > max ? value[..max] + "…[truncated]" : value;

    // ------------------------------------------------------------------
    // Full mode: one row per file with metadata.
    // ------------------------------------------------------------------

    private static void WriteFullRows(StringBuilder sb, ComposedDocument document)
    {
        sb.Append(string.Join(",", FullHeaders.Select(Escape)));
        sb.Append("\r\n");

        var index = 1;
        foreach (var fc in document.Files)
        {
            sb.Append(index.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(Escape(fc.File.AbsolutePath));
            sb.Append(',');
            sb.Append(fc.RawBytes.Length.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(Escape(fc.Sha256));
            sb.Append(',');
            sb.Append(Escape(fc.Encoding.WebName));
            sb.Append(',');
            sb.Append(fc.IsBinary ? "true" : "false");
            sb.Append(',');
            sb.Append(fc.IsUnreadable ? "true" : "false");
            sb.Append(',');
            sb.Append(Escape(BuildContentCell(fc)));
            sb.Append("\r\n");

            index++;
        }
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

    // ------------------------------------------------------------------

    private static string Escape(string value)
    {
        if (value.AsSpan().IndexOfAny(CharsToQuote) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
