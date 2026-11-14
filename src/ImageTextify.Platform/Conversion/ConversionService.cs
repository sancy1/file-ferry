// ============================================================================
// filepath: src/ImageTextify.Platform/Conversion/ConversionService.cs
// layer:    Platform / Infrastructure
// module:   ImageTextify.Platform.Conversion
// since:    Phase 11 (Document/Image Conversion)
// purpose:  Implements IConversionService. Dispatches a (source, target) pair
//           to the right per-format converter. Rejects unsupported pairs with
//           a specific, named reason.
//
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.IConversionService
//   Depends on : ImageFormatConverter, MarkdownDocxConverter,
//                MarkdownPdfConverter, ExcelConverter,
//                ImageTextifyPolicyNames.Conversion, IResilienceExecutor
//   Used by    : ImageTextifyServiceCollectionExtensions (DI);
//                FileHarvester.App.ViewModels.ConversionViewModel
//   See also   : IFormatConverter.cs, IConversionService.cs
//
// NOTES
//   - Supported matrix proven by Phase 11 spikes: images any-to-any
//     (PNG/JPG/JPEG/WEBP/ICO/GIF), MD <-> DOCX, MD -> PDF, XLSX create,
//     JSON <-> CSV.
//   - Unsupported pairs carry a specific reason (F-030 / F-031 / F-032),
//     never a generic failure.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;
using ImageTextify.Platform.Composition;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Errors;

namespace ImageTextify.Platform.Conversion;

/// <summary>
/// The single conversion dispatch behind the UI.
/// </summary>
internal sealed class ConversionService : IConversionService
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".ico", ".gif" };
    private static readonly ConversionFormat[] ImageFormats =
    {
        ConversionFormat.Png, ConversionFormat.Jpg, ConversionFormat.Jpeg,
        ConversionFormat.Webp, ConversionFormat.Ico, ConversionFormat.Gif
    };

    private readonly IResilienceExecutor _executor;

    /// <summary>Creates the service.</summary>
    /// <param name="executor">The resilience executor. Not null.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="executor"/> is null.</exception>
    public ConversionService(IResilienceExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ConversionPair>> EnumerateSupportedPairsAsync(CancellationToken cancellationToken)
    {
        var pairs = new List<ConversionPair>();
        foreach (var ext in ImageExtensions)
        {
            foreach (var fmt in ImageFormats)
            {
                pairs.Add(new ConversionPair(ext, fmt, ext.TrimStart('.') + " to " + fmt));
            }
        }
        pairs.Add(new ConversionPair(".md", ConversionFormat.Docx, "Markdown to Word"));
        pairs.Add(new ConversionPair(".md", ConversionFormat.Pdf, "Markdown to PDF"));
        pairs.Add(new ConversionPair(".md", ConversionFormat.Txt, "Markdown to Text"));
        pairs.Add(new ConversionPair(".docx", ConversionFormat.Md, "Word to Markdown"));
        pairs.Add(new ConversionPair(".docx", ConversionFormat.Txt, "Word to Text"));
        pairs.Add(new ConversionPair(".docx", ConversionFormat.Pdf, "Word to PDF"));
        pairs.Add(new ConversionPair(".txt", ConversionFormat.Md, "Text to Markdown"));
        pairs.Add(new ConversionPair(".txt", ConversionFormat.Docx, "Text to Word"));
        pairs.Add(new ConversionPair(".txt", ConversionFormat.Pdf, "Text to PDF"));
        pairs.Add(new ConversionPair(".txt", ConversionFormat.Csv, "Text to CSV"));
        pairs.Add(new ConversionPair(".txt", ConversionFormat.Json, "Text to JSON"));
        pairs.Add(new ConversionPair(".md", ConversionFormat.Csv, "Markdown to CSV"));
        pairs.Add(new ConversionPair(".md", ConversionFormat.Json, "Markdown to JSON"));
        pairs.Add(new ConversionPair(".csv", ConversionFormat.Txt, "CSV to Text"));
        pairs.Add(new ConversionPair(".json", ConversionFormat.Txt, "JSON to Text"));
        pairs.Add(new ConversionPair(".json", ConversionFormat.Csv, "JSON to CSV"));
        pairs.Add(new ConversionPair(".csv", ConversionFormat.Json, "CSV to JSON"));
        pairs.Add(new ConversionPair("*", ConversionFormat.Xlsx, "Any file to XLSX"));
        return Task.FromResult<IReadOnlyList<ConversionPair>>(pairs);
    }

    /// <inheritdoc/>
    public async Task<ConversionResult> ConvertAsync(
        ConversionRequest request,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.InputPath)) return ConversionResult.Fail("Input path is empty.");
        if (string.IsNullOrWhiteSpace(request.OutputPath)) return ConversionResult.Fail("Output path is empty.");
        var ext = Path.GetExtension(request.InputPath).ToLowerInvariant();

        // Reject the three dropped pairs with their named reasons FIRST, so the user
        // is told the truth about the free stack, not a generic "failed".
        if (ext == ".xlsx" && (request.TargetFormat == ConversionFormat.Csv || request.TargetFormat == ConversionFormat.Json))
            return ConversionResult.Fail("XLSX to " + request.TargetFormat + " is not supported by the free conversion stack (Phase 11 finding F-030).");
        // DOCX -> PDF is supported by chaining DOCX -> Markdown -> PDF
        // (TextDocumentConverter), so the old F-031 rejection is gone.
        if (ext == ".pdf" && (request.TargetFormat == ConversionFormat.Docx || request.TargetFormat == ConversionFormat.Md || request.TargetFormat == ConversionFormat.Txt))
            return ConversionResult.Fail("PDF to " + request.TargetFormat + " has no free reliable reader (Phase 11 finding F-032).");

        try
        {
            var result = await _executor.ExecuteAsync(
                ImageTextifyPolicyNames.Conversion,
                async ct => await DispatchAsync(ext, request, progress, ct).ConfigureAwait(false),
                fallback: null,
                idempotencyKey: null,
                timeBudgetMs: 60_000,
                ct: cancellationToken).ConfigureAwait(false);
            return result ?? ConversionResult.Fail("Conversion was not executed.");
        }
        catch (OperationCanceledException) { throw; }
        catch (ResilienceException ex)
        {
            return ConversionResult.Fail("Conversion failed: " + (ex.InnerException?.Message ?? ex.Message));
        }
    }

    private static Task<ConversionResult> DispatchAsync(
        string sourceExt, ConversionRequest r, IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new ConversionProgress("Converting", 0.0));
        if (Array.IndexOf(ImageExtensions, sourceExt) >= 0 && Array.IndexOf(ImageFormats, r.TargetFormat) >= 0)
            return ImageFormatConverter.ConvertAsync(r.InputPath, r.OutputPath, r.TargetFormat, ct);
        if (sourceExt == ".md" && r.TargetFormat == ConversionFormat.Docx)
            return MarkdownDocxConverter.MarkdownToDocxAsync(r.InputPath, r.OutputPath, ct);
        if (sourceExt == ".docx" && r.TargetFormat == ConversionFormat.Md)
            return MarkdownDocxConverter.DocxToMarkdownAsync(r.InputPath, r.OutputPath, ct);
        if (sourceExt == ".md" && r.TargetFormat == ConversionFormat.Pdf)
            return MarkdownPdfConverter.ConvertAsync(r.InputPath, r.OutputPath, ct);
        if (sourceExt == ".txt" && r.TargetFormat == ConversionFormat.Md)
            return TextDocumentConverter.CopyAsync(r.InputPath, r.OutputPath, ct);
        if (sourceExt == ".txt" && r.TargetFormat == ConversionFormat.Docx)
            return TextDocumentConverter.TextToDocxAsync(r.InputPath, r.OutputPath, ct);
        if (sourceExt == ".txt" && r.TargetFormat == ConversionFormat.Pdf)
            return TextDocumentConverter.TextToPdfAsync(r.InputPath, r.OutputPath, ct);
        if ((sourceExt == ".txt" || sourceExt == ".md") && r.TargetFormat == ConversionFormat.Csv)
            return TextDocumentConverter.TextToCsvAsync(r.InputPath, r.OutputPath, ct);
        if ((sourceExt == ".txt" || sourceExt == ".md") && r.TargetFormat == ConversionFormat.Json)
            return TextDocumentConverter.TextToJsonAsync(r.InputPath, r.OutputPath, ct);
        if (sourceExt == ".md" && r.TargetFormat == ConversionFormat.Txt)
            return TextDocumentConverter.CopyAsync(r.InputPath, r.OutputPath, ct);
        if (sourceExt == ".docx" && r.TargetFormat == ConversionFormat.Txt)
            return TextDocumentConverter.DocxToTextAsync(r.InputPath, r.OutputPath, ct);
        if (sourceExt == ".docx" && r.TargetFormat == ConversionFormat.Pdf)
            return TextDocumentConverter.DocxToPdfAsync(r.InputPath, r.OutputPath, ct);
        if ((sourceExt == ".csv" || sourceExt == ".json") && r.TargetFormat == ConversionFormat.Txt)
            return TextDocumentConverter.CopyAsync(r.InputPath, r.OutputPath, ct);
        if (sourceExt == ".json" && r.TargetFormat == ConversionFormat.Csv)
            return ExcelConverter.JsonToCsvAsync(r.InputPath, r.OutputPath, ct);
        if (sourceExt == ".csv" && r.TargetFormat == ConversionFormat.Json)
            return ExcelConverter.CsvToJsonAsync(r.InputPath, r.OutputPath, ct);
        if (r.TargetFormat == ConversionFormat.Xlsx)
            return ExcelConverter.ToXlsxAsync(r.InputPath, r.OutputPath, ct);
        return Task.FromResult(ConversionResult.Fail(
            "The conversion " + sourceExt + " to " + r.TargetFormat + " is not supported by this build."));
    }
}
