// ============================================================================
// filepath: src/ImageTextify.Platform/Conversion/ExcelConverter.cs
// layer:    Platform / Infrastructure
// module:   ImageTextify.Platform.Conversion
// since:    Phase 11 (Document/Image Conversion)
// purpose:  Excel-workbook create/save and JSON<->CSV via Aspose.Cells_FOSS
//           and SuperConvert.
//
// RELATIONSHIPS
//   Depends on : Aspose.Cells_FOSS, SuperConvert
//   Used by    : ConversionService
//   See also   : Phase 11 Spike 3 (PASS create XLSX, JSON->CSV, CSV->JSON)
//
// NOTES
//   - Aspose.Cells_FOSS SaveFormat exposes Xlsx only. XLSX->CSV/JSON not in the
//     free build (finding F-030); ConversionService rejects those pairs.
//   - SuperConvert: ToCsv(jsonData, path, fileName, separator) treats path as
//     the DIRECTORY and fileName as the base name. CsvToJson(filePath) takes
//     the FULL file path.
// ============================================================================

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Aspose.Cells_FOSS;
using ImageTextify.Core.Abstractions;
using SuperConvert.Extensions;

namespace ImageTextify.Platform.Conversion;

/// <summary>
/// Excel + JSON/CSV converter over Aspose.Cells_FOSS and SuperConvert.
/// </summary>
internal static class ExcelConverter
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".csv", ".json", ".log", ".xml",
        ".html", ".htm", ".yaml", ".yml", ".ini", ".cfg", ".srt", ".vtt",
    };

    /// <summary>Creates an XLSX destination file. Atomic.
    /// Text inputs are written as real cell content (CSV inputs become
    /// rows/columns); other inputs keep the source-label stub.
    /// </summary>
    /// <param name="inputPath">Source path for the label.</param>
    /// <param name="outputPath">The .xlsx destination.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>The conversion result.</returns>
    public static Task<ConversionResult> ToXlsxAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath)) return Task.FromResult(ConversionResult.Fail("Input file not found: " + inputPath));
        var tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var wb = new Workbook();
            var ws = wb.Worksheets[0];
            var ext = Path.GetExtension(inputPath);
            if (TextExtensions.Contains(ext))
            {
                var text = File.ReadAllText(inputPath);
                if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
                    WriteCsvRows(ws, text, cancellationToken);
                else
                    WriteTextLines(ws, text, cancellationToken);
            }
            else
            {
                ws.Cells["A1"].PutValue("source");
                ws.Cells["B1"].PutValue(Path.GetFileName(inputPath));
            }
            wb.Save(tempPath, SaveFormat.Xlsx);
            File.Move(tempPath, outputPath, overwrite: true);
            return Task.FromResult(ConversionResult.Ok(outputPath));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Task.FromResult(ConversionResult.Fail("To XLSX failed: " + ex.Message)); }
        finally { try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { } }
    }

    private static void WriteTextLines(Worksheet ws, string text, CancellationToken ct)
    {
        var row = 0;
        foreach (var line in text.Split('\n'))
        {
            ct.ThrowIfCancellationRequested();
            ws.Cells[row, 0].PutValue(line.TrimEnd('\r'));
            row++;
        }
    }

    private static void WriteCsvRows(Worksheet ws, string text, CancellationToken ct)
    {
        var row = 0;
        foreach (var line in text.Split('\n'))
        {
            ct.ThrowIfCancellationRequested();
            if (line.Length == 0) continue;
            var fields = ParseCsvLine(line.TrimEnd('\r'));
            for (var col = 0; col < fields.Count; col++)
                ws.Cells[row, col].PutValue(fields[col]);
            row++;
        }
    }

    // Minimal RFC-4180-ish parser: handles quoted fields, escaped quotes,
    // and commas inside quotes. Multi-line quoted fields are not supported
    // (each physical line is one record).
    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }

    /// <summary>Converts JSON to CSV via SuperConvert. Atomic.
    /// </summary>
    /// <param name="inputPath">The .json file path.</param>
    /// <param name="outputPath">The .csv destination.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>The conversion result.</returns>
    public static Task<ConversionResult> JsonToCsvAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath)) return Task.FromResult(ConversionResult.Fail("Input file not found: " + inputPath));
        var dir = Path.GetDirectoryName(outputPath) ?? Path.GetTempPath();
        var baseName = Path.GetFileNameWithoutExtension(outputPath);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = File.ReadAllText(inputPath);
            var produced = SuperConvert.Extensions.ExcelConverter.ToCsv(json, dir, baseName, ',');
            if (File.Exists(produced) && !string.Equals(produced, outputPath, StringComparison.OrdinalIgnoreCase))
                File.Move(produced, outputPath, overwrite: true);
            return Task.FromResult(ConversionResult.Ok(outputPath));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Task.FromResult(ConversionResult.Fail("JSON to CSV failed: " + ex.Message)); }
    }

    /// <summary>Converts CSV to JSON via SuperConvert. Atomic.
    /// </summary>
    /// <param name="inputPath">The .csv file path.</param>
    /// <param name="outputPath">The .json destination.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>The conversion result.</returns>
    public static Task<ConversionResult> CsvToJsonAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath)) return Task.FromResult(ConversionResult.Fail("Input file not found: " + inputPath));
        var tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = SuperConvert.Extensions.ExcelConverter.CsvToJson(inputPath);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, outputPath, overwrite: true);
            return Task.FromResult(ConversionResult.Ok(outputPath));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Task.FromResult(ConversionResult.Fail("CSV to JSON failed: " + ex.Message)); }
        finally { try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { } }
    }
}
