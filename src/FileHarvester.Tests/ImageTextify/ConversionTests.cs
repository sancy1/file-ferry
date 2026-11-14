// ============================================================================
// filepath: src/FileHarvester.Tests/ImageTextify/ConversionTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 11 (Document/Image Conversion)
// purpose:  File-based tests for the conversion slice. Each test writes a
//           small real input file into a temp directory, runs the real
//           ConversionService (with a stub resilience executor), asserts the
//           produced output, and cleans up. Real files prove the pipeline;
//           a mock would not.
// RELATIONSHIPS
//   Implements : n/a (test class)
//   Depends on : ConversionService (internal; InternalsVisibleTo FileHarvester.Tests),
//                IConversionService, ConversionRequest, ConversionResult,
//                ConversionFormat, IResilienceExecutor
//   Used by    : test runner
//   See also   : ConversionService.cs, IConversionService.cs,
//                CameraCaptureTests.cs (naming and header style)
// NOTES
//   - Supported-pair tests (1-9) run the real converter via the real dispatch.
//     Unsupported-pair tests (10-12) assert the specific named reason F-030/
//     F-031/F-032, never a generic failure.
//   - Cancellation test (13) proves no output file is left behind.
//   - Every test disposes the temp directory in finally. No test reads a
//     shared fixture; each writes its own inputs. No network, no clocks.
// ============================================================================

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;
using ImageTextify.Platform.Conversion;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Errors;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// File-based acceptance tests for the conversion slice.
/// </summary>
public sealed class ConversionTests
{
    private static ConversionService NewService() => new(new StubExecutor());

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "convtest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // A minimal valid PNG (1x1 transparent) as bytes.
    private static byte[] TinyPng() => new byte[]
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    };

    // A minimal valid JPEG produced by the same library the converter uses,
    // so the test exercises the real JPEG decode path (not a hand-rolled file).
    private static byte[] TinyJpeg()
    {
        var tmpPng = Path.Combine(Path.GetTempPath(), "tiny_" + Guid.NewGuid().ToString("N") + ".png");
        var tmpJpg = tmpPng + ".jpg";
        try
        {
            File.WriteAllBytes(tmpPng, TinyPng());
            using (var image = Meziantou.Framework.Imaging.Image.Load(tmpPng))
            {
                Meziantou.Framework.Imaging.ImageProcessingExtensions.Flatten(
                    image, new Meziantou.Framework.Imaging.Rgba64(65535, 65535, 65535, 65535), CancellationToken.None);
                image.Save(tmpJpg, new Meziantou.Framework.Imaging.Formats.JpegEncoder());
            }
            return File.ReadAllBytes(tmpJpg);
        }
        finally
        {
            try { File.Delete(tmpPng); } catch { }
            try { File.Delete(tmpJpg); } catch { }
        }
    }

    private static string WriteInput(string dir, string name, byte[] bytes)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static async Task<ConversionResult> Run(string dir, string inputName, byte[] bytes, string outName, ConversionFormat target)
    {
        var svc = NewService();
        var input = WriteInput(dir, inputName, bytes);
        var output = Path.Combine(dir, outName);
        return await svc.ConvertAsync(new ConversionRequest(input, output, target), null, CancellationToken.None);
    }

    [Fact]
    public async Task PngToIcoSucceeds()
    {
        var dir = NewTempDir();
        try
        {
            var r = await Run(dir, "in.png", TinyPng(), "out.ico", ConversionFormat.Ico);
            Assert.True(r.Success, r.ErrorMessage);
            Assert.True(File.Exists(r.OutputPath!));
            var bytes = File.ReadAllBytes(r.OutputPath!);
            Assert.True(bytes.Length > 4);
            Assert.Equal((byte)0x00, bytes[0]);
            Assert.Equal((byte)0x00, bytes[1]);
            Assert.Equal((byte)0x01, bytes[2]);
            Assert.Equal((byte)0x00, bytes[3]);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task JpgToIcoSucceeds()
    {
        var dir = NewTempDir();
        try
        {
            var r = await Run(dir, "in.jpg", TinyJpeg(), "out.ico", ConversionFormat.Ico);
            Assert.True(r.Success, r.ErrorMessage);
            Assert.True(File.Exists(r.OutputPath!));
            var bytes = File.ReadAllBytes(r.OutputPath!);
            Assert.True(bytes.Length > 4);
            Assert.Equal((byte)0x00, bytes[0]);
            Assert.Equal((byte)0x00, bytes[1]);
            Assert.Equal((byte)0x01, bytes[2]);
            Assert.Equal((byte)0x00, bytes[3]);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task PngToWebpSucceeds()
    {
        var dir = NewTempDir();
        try
        {
            var r = await Run(dir, "in.png", TinyPng(), "out.webp", ConversionFormat.Webp);
            Assert.True(r.Success, r.ErrorMessage);
            var bytes = File.ReadAllBytes(r.OutputPath!);
            Assert.True(bytes.Length > 12);
            Assert.Equal((byte)0x52, bytes[0]); // 'R'
            Assert.Equal((byte)0x49, bytes[1]); // 'I'
            Assert.Equal((byte)0x46, bytes[2]); // 'F'
            Assert.Equal((byte)0x46, bytes[3]); // 'F'
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task PngToGifSucceeds()
    {
        var dir = NewTempDir();
        try
        {
            var r = await Run(dir, "in.png", TinyPng(), "out.gif", ConversionFormat.Gif);
            Assert.True(r.Success, r.ErrorMessage);
            var bytes = File.ReadAllBytes(r.OutputPath!);
            Assert.True(bytes.Length > 6);
            Assert.Equal((byte)0x47, bytes[0]); // G
            Assert.Equal((byte)0x49, bytes[1]); // I
            Assert.Equal((byte)0x46, bytes[2]); // F
            Assert.Equal((byte)0x38, bytes[3]); // 8
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task MdToDocxSucceeds()
    {
        var dir = NewTempDir();
        try
        {
            var md = Encoding.UTF8.GetBytes("# Title\n\nHello **world**.\n");
            var r = await Run(dir, "in.md", md, "out.docx", ConversionFormat.Docx);
            Assert.True(r.Success, r.ErrorMessage);
            var bytes = File.ReadAllBytes(r.OutputPath!);
            Assert.True(bytes.Length > 0);
            Assert.Equal((byte)0x50, bytes[0]); // P
            Assert.Equal((byte)0x4B, bytes[1]); // K
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task DocxToMdSucceeds()
    {
        var dir = NewTempDir();
        try
        {
            // First create a DOCX from Markdown, then round-trip back.
            var md = Encoding.UTF8.GetBytes("# RoundTrip\n\nContent goes here.\n");
            var docx = await Run(dir, "rt.md", md, "rt.docx", ConversionFormat.Docx);
            Assert.True(docx.Success, docx.ErrorMessage);
            var svc = NewService();
            var mdOut = Path.Combine(dir, "rt-back.md");
            var back = await svc.ConvertAsync(
                new ConversionRequest(docx.OutputPath!, mdOut, ConversionFormat.Md),
                null, CancellationToken.None);
            Assert.True(back.Success, back.ErrorMessage);
            var text = File.ReadAllText(mdOut);
            Assert.Contains("RoundTrip", text);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task MdToPdfSucceeds()
    {
        var dir = NewTempDir();
        try
        {
            var md = Encoding.UTF8.GetBytes("# PdfTest\n\nHello.\n");
            var r = await Run(dir, "in.md", md, "out.pdf", ConversionFormat.Pdf);
            Assert.True(r.Success, r.ErrorMessage);
            var bytes = File.ReadAllBytes(r.OutputPath!);
            Assert.True(bytes.Length > 4);
            Assert.Equal((byte)0x25, bytes[0]); // %
            Assert.Equal((byte)0x50, bytes[1]); // P
            Assert.Equal((byte)0x44, bytes[2]); // D
            Assert.Equal((byte)0x46, bytes[3]); // F
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task JsonToCsvSucceeds()
    {
        var dir = NewTempDir();
        try
        {
            var json = Encoding.UTF8.GetBytes("[{\"name\":\"alpha\",\"value\":1}]");
            var r = await Run(dir, "in.json", json, "out.csv", ConversionFormat.Csv);
            Assert.True(r.Success, r.ErrorMessage);
            var text = File.ReadAllText(r.OutputPath!);
            Assert.Contains("alpha", text);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task CsvToJsonSucceeds()
    {
        var dir = NewTempDir();
        try
        {
            var csv = Encoding.UTF8.GetBytes("name,value\nalpha,1\n");
            var r = await Run(dir, "in.csv", csv, "out.json", ConversionFormat.Json);
            Assert.True(r.Success, r.ErrorMessage);
            var text = File.ReadAllText(r.OutputPath!);
            Assert.Contains("alpha", text);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task ToXlsxSucceeds()
    {
        var dir = NewTempDir();
        try
        {
            var src = Encoding.UTF8.GetBytes("dummy");
            var r = await Run(dir, "in.txt", src, "out.xlsx", ConversionFormat.Xlsx);
            Assert.True(r.Success, r.ErrorMessage);
            var bytes = File.ReadAllBytes(r.OutputPath!);
            Assert.True(bytes.Length > 0);
            Assert.Equal((byte)0x50, bytes[0]); // P
            Assert.Equal((byte)0x4B, bytes[1]); // K
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task UnsupportedXlsxToJsonIsRejectedWithF030()
    {
        var dir = NewTempDir();
        try
        {
            var src = Encoding.UTF8.GetBytes("not really an xlsx");
            var r = await Run(dir, "in.xlsx", src, "out.json", ConversionFormat.Json);
            Assert.False(r.Success);
            Assert.NotNull(r.ErrorMessage);
            Assert.Contains("F-030", r.ErrorMessage!);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task DocxToPdfSucceeds()
    {
        var dir = NewTempDir();
        try
        {
            // Build a real DOCX first, then convert it to PDF via the
            // DOCX -> Markdown -> PDF chain.
            var md = Encoding.UTF8.GetBytes("# DocxPdf\n\nHello from Word.\n");
            var docx = await Run(dir, "in.md", md, "in.docx", ConversionFormat.Docx);
            Assert.True(docx.Success, docx.ErrorMessage);
            var svc = NewService();
            var pdfOut = Path.Combine(dir, "out.pdf");
            var r = await svc.ConvertAsync(
                new ConversionRequest(docx.OutputPath!, pdfOut, ConversionFormat.Pdf),
                null, CancellationToken.None);
            Assert.True(r.Success, r.ErrorMessage);
            var bytes = File.ReadAllBytes(r.OutputPath!);
            Assert.True(bytes.Length > 4);
            Assert.Equal((byte)0x25, bytes[0]); // %
            Assert.Equal((byte)0x50, bytes[1]); // P
            Assert.Equal((byte)0x44, bytes[2]); // D
            Assert.Equal((byte)0x46, bytes[3]); // F
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task TxtToDocxSucceeds()
    {
        var dir = NewTempDir();
        try
        {
            var txt = Encoding.UTF8.GetBytes("# FromText\n\nPlain text body.\n");
            var r = await Run(dir, "in.txt", txt, "out.docx", ConversionFormat.Docx);
            Assert.True(r.Success, r.ErrorMessage);
            var bytes = File.ReadAllBytes(r.OutputPath!);
            Assert.Equal((byte)0x50, bytes[0]); // P
            Assert.Equal((byte)0x4B, bytes[1]); // K
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task TxtToPdfSucceeds()
    {
        var dir = NewTempDir();
        try
        {
            var txt = Encoding.UTF8.GetBytes("# FromText\n\nPlain text body.\n");
            var r = await Run(dir, "in.txt", txt, "out.pdf", ConversionFormat.Pdf);
            Assert.True(r.Success, r.ErrorMessage);
            var bytes = File.ReadAllBytes(r.OutputPath!);
            Assert.Equal((byte)0x25, bytes[0]); // %
            Assert.Equal((byte)0x50, bytes[1]); // P
            Assert.Equal((byte)0x44, bytes[2]); // D
            Assert.Equal((byte)0x46, bytes[3]); // F
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task MdToTxtSucceeds()
    {
        var dir = NewTempDir();
        try
        {
            var md = Encoding.UTF8.GetBytes("# Title\n\nBody text.\n");
            var r = await Run(dir, "in.md", md, "out.txt", ConversionFormat.Txt);
            Assert.True(r.Success, r.ErrorMessage);
            var text = File.ReadAllText(r.OutputPath!);
            Assert.Contains("Body text.", text);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task DocxToTxtSucceeds()
    {
        var dir = NewTempDir();
        try
        {
            var md = Encoding.UTF8.GetBytes("# DocxTxt\n\nWord body.\n");
            var docx = await Run(dir, "in.md", md, "in.docx", ConversionFormat.Docx);
            Assert.True(docx.Success, docx.ErrorMessage);
            var svc = NewService();
            var txtOut = Path.Combine(dir, "out.txt");
            var r = await svc.ConvertAsync(
                new ConversionRequest(docx.OutputPath!, txtOut, ConversionFormat.Txt),
                null, CancellationToken.None);
            Assert.True(r.Success, r.ErrorMessage);
            var text = File.ReadAllText(r.OutputPath!);
            Assert.Contains("Word body.", text);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task LargePngToIcoSucceeds()
    {
        var dir = NewTempDir();
        try
        {
            // 800x600 PNG exceeds the 256px ICO canvas limit; the converter
            // must downscale instead of failing (runtime defect fix).
            var tmpPng = Path.Combine(dir, "big.png");
            File.WriteAllBytes(tmpPng, TinyPng());
            using (var tiny = Meziantou.Framework.Imaging.Image.Load(tmpPng))
            {
                Meziantou.Framework.Imaging.ImageProcessingExtensions.Resize(
                    tiny, new Meziantou.Framework.Imaging.ResizeOptions(800, 600), CancellationToken.None);
                tiny.Save(tmpPng, new Meziantou.Framework.Imaging.Formats.PngEncoder());
            }
            var svc = NewService();
            var icoOut = Path.Combine(dir, "out.ico");
            var r = await svc.ConvertAsync(
                new ConversionRequest(tmpPng, icoOut, ConversionFormat.Ico),
                null, CancellationToken.None);
            Assert.True(r.Success, r.ErrorMessage);
            var bytes = File.ReadAllBytes(r.OutputPath!);
            Assert.Equal((byte)0x00, bytes[0]);
            Assert.Equal((byte)0x00, bytes[1]);
            Assert.Equal((byte)0x01, bytes[2]);
            Assert.Equal((byte)0x00, bytes[3]);
            // The payload must be a DIB, not a PNG-compressed entry: Windows
            // Photos refuses PNG-payload ICOs. Image data offset is at byte 18.
            var dataOffset = BitConverter.ToInt32(bytes, 18);
            Assert.True(dataOffset > 0 && dataOffset < bytes.Length);
            Assert.NotEqual((byte)0x89, bytes[dataOffset]); // not PNG magic
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task TxtToCsvSucceeds()
    {
        var dir = NewTempDir();
        try
        {
            var txt = Encoding.UTF8.GetBytes("alpha\nbeta,gamma\n\"quoted\" line\n");
            var r = await Run(dir, "in.txt", txt, "out.csv", ConversionFormat.Csv);
            Assert.True(r.Success, r.ErrorMessage);
            var csv = File.ReadAllText(r.OutputPath!);
            Assert.Contains("alpha", csv);
            Assert.Contains("\"beta,gamma\"", csv); // comma forces quoting
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task TxtToJsonSucceeds()
    {
        var dir = NewTempDir();
        try
        {
            var txt = Encoding.UTF8.GetBytes("alpha\nbeta\n");
            var r = await Run(dir, "in.txt", txt, "out.json", ConversionFormat.Json);
            Assert.True(r.Success, r.ErrorMessage);
            var json = File.ReadAllText(r.OutputPath!);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            Assert.Equal(2, doc.RootElement.GetArrayLength());
            Assert.Equal("alpha", doc.RootElement[0].GetString());
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task TxtToXlsxContainsBodyText()
    {
        var dir = NewTempDir();
        try
        {
            var txt = Encoding.UTF8.GetBytes("first line\nsecond line\n");
            var r = await Run(dir, "in.txt", txt, "out.xlsx", ConversionFormat.Xlsx);
            Assert.True(r.Success, r.ErrorMessage);
            // Read the workbook back and prove the body text landed in cells,
            // not just the old "source | filename" stub.
            var wb = new Aspose.Cells_FOSS.Workbook(r.OutputPath!);
            var ws = wb.Worksheets[0];
            Assert.Equal("first line", ws.Cells[0, 0].StringValue);
            Assert.Equal("second line", ws.Cells[1, 0].StringValue);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task CsvToXlsxContainsColumns()
    {
        var dir = NewTempDir();
        try
        {
            var csv = Encoding.UTF8.GetBytes("name,age\nalice,30\nbob,25\n");
            var r = await Run(dir, "in.csv", csv, "out.xlsx", ConversionFormat.Xlsx);
            Assert.True(r.Success, r.ErrorMessage);
            var wb = new Aspose.Cells_FOSS.Workbook(r.OutputPath!);
            var ws = wb.Worksheets[0];
            Assert.Equal("name", ws.Cells[0, 0].StringValue);
            Assert.Equal("age", ws.Cells[0, 1].StringValue);
            Assert.Equal("alice", ws.Cells[1, 0].StringValue);
            Assert.Equal("30", ws.Cells[1, 1].StringValue);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task UnsupportedPdfToDocxIsRejectedWithF032()
    {
        var dir = NewTempDir();
        try
        {
            var src = Encoding.UTF8.GetBytes("%PDF-1.4");
            var r = await Run(dir, "in.pdf", src, "out.docx", ConversionFormat.Docx);
            Assert.False(r.Success);
            Assert.Contains("F-032", r.ErrorMessage!);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task CancellationProducesNoOutputFile()
    {
        var dir = NewTempDir();
        try
        {
            var svc = NewService();
            var input = WriteInput(dir, "in.png", TinyPng());
            var output = Path.Combine(dir, "out.ico");
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await svc.ConvertAsync(new ConversionRequest(input, output, ConversionFormat.Ico), null, cts.Token));
            Assert.False(File.Exists(output));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>
    /// Minimal <see cref="IResilienceExecutor"/> that runs the delegate once
    /// with the caller's token and returns the result. Sufficient for tests;
    /// the real policy behavior is proven elsewhere (resilience tests).
    /// </summary>
    private sealed class StubExecutor : IResilienceExecutor
    {
        public async Task<T> ExecuteAsync<T>(
            string policyName,
            Func<CancellationToken, Task<T>> operation,
            Func<CancellationToken, Task<T>>? fallback,
            string? idempotencyKey,
            int? timeBudgetMs,
            CancellationToken ct)
        {
            return await operation(ct).ConfigureAwait(false);
        }

        public async Task ExecuteAsync(
            string policyName,
            Func<CancellationToken, Task> operation,
            Func<CancellationToken, Task>? fallback,
            string? idempotencyKey,
            int? timeBudgetMs,
            CancellationToken ct)
        {
            await operation(ct).ConfigureAwait(false);
        }
    }
}
