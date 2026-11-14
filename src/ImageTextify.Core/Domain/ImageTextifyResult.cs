// ============================================================================
// FILE:     src/ImageTextify.Core/Domain/ImageTextifyResult.cs
// PURPOSE:  The outcome of processing one source file: the extracted
//           text, the page or segment information, the confidence,
//           the warnings, and the processing metadata. Immutable.
// RELATION: Produced by the extraction pipeline. Consumed by the UI
//           result pane and by ImageTextifyDocument for export.
// ============================================================================

namespace ImageTextify.Core.Domain;

/// <summary>
/// The outcome of processing one source file.
/// </summary>
/// <param name="Source">
/// The source file this result was produced from. Not null.
/// </param>
/// <param name="Text">
/// The extracted, cleaned, editable text. Never null; may be empty when
/// the source contained no detectable text. Never invented; uncertain
/// regions use the literal token <c>[unclear]</c> where the engine
/// cannot resolve them.
/// </param>
/// <param name="PageCount">
/// The number of pages for a PDF, the number of utterances for audio,
/// or 1 for an image or a video. At least 1.
/// </param>
/// <param name="Timestamps">
/// The segment timestamps for audio and video, in the order the
/// segments appear in <see cref="Text"/>. Null for images and PDFs, and
/// for audio or video when the transcriber did not produce timestamps.
/// </param>
/// <param name="Confidence">
/// The engine-reported confidence in the range zero to one, or null
/// when the engine does not report one. Never fabricated; a null
/// confidence is honest.
/// </param>
/// <param name="Warnings">
/// Quality and degradation warnings collected during processing, for
/// example low resolution, possible motion blur, or low confidence.
/// Never null; empty when there were none.
/// </param>
/// <param name="Duration">
/// The processing time for this file. Non-negative.
/// </param>
/// <param name="Engine">
/// The engine identifier, for example <c>onnx-paddleocr</c> or
/// <c>whisper-net</c>. Never null or empty.
/// </param>
/// <param name="ModelId">
/// The identifier of the model used, when the engine used one. Null
/// when the engine does not use a model.
/// </param>
public sealed record ImageTextifyResult(
    ImageTextifySourceFile Source,
    string Text,
    int PageCount,
    IReadOnlyList<TimeSpan>? Timestamps,
    double? Confidence,
    IReadOnlyList<string> Warnings,
    TimeSpan Duration,
    string Engine,
    string? ModelId);