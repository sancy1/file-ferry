// ============================================================================
// filepath: src/ImageTextify.Core/Abstractions/IFormatConverter.cs
// layer:    Contracts
// module:   ImageTextify.Core.Abstractions
// since:    Phase 11 (Document/Image Conversion)
// purpose:  Contract for converting one file from a source format to a target
//           format. Local only. Implementations live in ImageTextify.Platform
//           and are dispatched by ConversionService.
//
// RELATIONSHIPS
//   Implements : n/a (interface)
//   Depends on : n/a (BCL only)
//   Used by    : ImageTextify.Platform.Conversion.ConversionService (dispatch),
//                ImageTextify.Platform.Conversion.*Converter (implementations),
//                FileHarvester.App.ViewModels.ConversionViewModel
//   See also   : Phase 11 design plan (proven matrix from the three spikes);
//                findings F-030 (XLSX->CSV/JSON not in FOSS), F-031 (DOCX->PDF
//                commercial), F-032 (PDF->DOCX no free reliable library)
//
// NOTES
//   - The proven free matrix (from Phase 11 spikes):
//     Images: PNG/JPG/JPEG/WEBP/ICO/GIF, any-to-any (Meziantou.Framework.Imaging).
//     Documents: MD<->DOCX (DocSharp), MD->PDF (TinyPdf),
//                XLSX create/save (Aspose.Cells.FOSS), JSON<->CSV (SuperConvert).
//   - Pairs NOT covered by the free libraries (the implementation rejects them
//     with an explicit reason, never a generic failure):
//       XLSX -> CSV/JSON : not exposed by Aspose.Cells.FOSS SaveFormat (F-030).
//       DOCX -> PDF       : requires QuestPDF (commercial) (F-031).
//       PDF  -> DOCX      : no free reliable library (F-032).
//   - The converter is synchronous under the hood; the IProgress<ConversionProgress>
//     exists so the UI can show determinate progress per stage without threads.
// ============================================================================

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// A file converter: transforms one input file into one output file in a target
/// format. Local only. Not a batch or multi-page orchestrator; one call, one file.
/// </summary>
public interface IFormatConverter
{
    /// <summary>
    /// Converts the file at <see cref="ConversionRequest.InputPath"/> to the
    /// format at <see cref="ConversionRequest.OutputPath"/>.
    /// </summary>
    /// <param name="request">The conversion request. Not null.</param>
    /// <param name="progress">
    /// Optional progress sink. When supplied, receives at least a start and an
    /// end event. Not required for correctness.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the conversion. On cancellation, the output file must NOT exist
    /// (the implementation writes to a temp file and renames atomically).
    /// </param>
    /// <returns>
    /// The conversion result. A failure carries a specific reason; a pair
    /// outside the proven matrix is rejected with the reason named in the
    /// class NOTES (F-030/F-031/F-032), never a generic error.
    /// </returns>
    Task<ConversionResult> ConvertAsync(
        ConversionRequest request,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// One conversion request: an input path, an output path, a target format.
/// </summary>
/// <param name="InputPath">The absolute path of the input file. Not null.</param>
/// <param name="OutputPath">
/// The absolute path of the output file. Not null. The implementation creates
/// or overwrites this file on success and creates no file on failure.
/// </param>
/// <param name="TargetFormat">
/// The desired output format. The implementation decides whether the pair
/// (input extension, target) is supported and rejects unsupported pairs with
/// a specific reason.
/// </param>
public sealed record ConversionRequest(
    string InputPath,
    string OutputPath,
    ConversionFormat TargetFormat);

/// <summary>
/// The outcome of a conversion attempt.
/// </summary>
/// <param name="Success">True if the output file was produced.</param>
/// <param name="OutputPath">
/// The absolute path of the produced file when <see cref="Success"/> is true;
/// null otherwise.
/// </param>
/// <param name="ErrorMessage">
/// A specific, user-facing reason when <see cref="Success"/> is false. Never
/// generic. Names the pair when unsupported ("XLSX to JSON is not supported by
/// the free conversion stack; see Phase 11 finding F-030").
/// </param>
public sealed record ConversionResult(
    bool Success,
    string? OutputPath,
    string? ErrorMessage)
{
    /// <summary>Creates a successful result with the produced path.</summary>
    /// <param name="outputPath">The produced file path. Not null.</param>
    /// <returns>A result with <see cref="Success"/> = true.</returns>
    public static ConversionResult Ok(string outputPath)
        => new(true, outputPath, null);

    /// <summary>Creates a failed result with a specific reason.</summary>
    /// <param name="reason">A user-facing reason. Not null or empty.</param>
    /// <returns>A result with <see cref="Success"/> = false.</returns>
    public static ConversionResult Fail(string reason)
        => new(false, null, reason);
}

/// <summary>
/// Progress for a single conversion.
/// </summary>
/// <param name="Stage">A short, user-facing label for the current stage.</param>
/// <param name="FractionComplete">
/// Completion in [0, 1] when the implementation can measure it; null otherwise.
/// </param>
public sealed record ConversionProgress(string Stage, double? FractionComplete);

/// <summary>
/// The output formats the app can produce. Enum (not a string) so the UI dropdown
/// and the dispatch in ConversionService share one closed set.
/// </summary>
public enum ConversionFormat
{
    /// <summary>Portable Network Graphics.</summary>
    Png = 0,
    /// <summary>JPEG (JFIF).</summary>
    Jpg = 1,
    /// <summary>JPEG (JFIF) with the alternate extension.</summary>
    Jpeg = 2,
    /// <summary>WebP.</summary>
    Webp = 3,
    /// <summary>Windows Icon.</summary>
    Ico = 4,
    /// <summary>Graphics Interchange Format.</summary>
    Gif = 5,
    /// <summary>Word document (Office Open XML).</summary>
    Docx = 6,
    /// <summary>Markdown.</summary>
    Md = 7,
    /// <summary>Portable Document Format (write-only via TinyPdf today).</summary>
    Pdf = 8,
    /// <summary>Plain text.</summary>
    Txt = 9,
    /// <summary>Microsoft Excel workbook (Office Open XML).</summary>
    Xlsx = 10,
    /// <summary>JSON.</summary>
    Json = 11,
    /// <summary>Comma-separated values.</summary>
    Csv = 12,
}
