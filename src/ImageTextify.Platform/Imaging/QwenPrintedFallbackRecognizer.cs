// filepath: src/ImageTextify.Platform/Imaging/QwenPrintedFallbackRecognizer.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Imaging
// since:    Phase 2.4e
// purpose:  Printed-text fallback wrapper around IQwenVisionService.
//
// RELATIONSHIPS
//   Implements : n/a (concrete class)
//   Depends on : IQwenVisionService
//   Used by    : ImageTextify.Platform.OnnxOcrEngine (Phase 2.4e wiring)
//   See also   : QwenHandwritingRecognizer.cs, IQwenVisionService.cs
//
// NOTES
//   - Acceptance rule: the cleaned Qwen output must be strictly longer
//     than the PP-OCR text for the same crop. This is a deliberate proxy
//     for confidence (IQwenVisionService returns text only, no score).
//     See finding ITX-B9-001 for the interface change that would replace
//     this proxy with a real confidence.
//   - Does not rewrite, summarize, translate, or correct wording.
//   - Returns empty string when the Qwen output is rejected or empty.
//   - Not thread-safe for concurrent calls. Delegates to IQwenVisionService
//     which serializes with a SemaphoreSlim.
//   - Performance: inherits from IQwenVisionService (~35 seconds per call
//     on CPU). The engine must show progress or run on a background thread.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// Printed-text fallback wrapper around <see cref="IQwenVisionService"/>.
/// </summary>
/// <remarks>
/// Sends a printed-text crop to Qwen with a printed-text prompt, strips
/// trailing chat tokens and outer whitespace, and returns the text only
/// when it is strictly longer than the PP-OCR result for the same crop.
/// Does not rewrite or correct the model output.
/// </remarks>
public sealed class QwenPrintedFallbackRecognizer
{
    /// <summary>
    /// The prompt sent to Qwen for printed-text fallback recognition.
    /// </summary>
    internal const string PrintedPrompt =
        "Read the printed text in this image. Return only the text you see. " +
        "Do not invent characters. Do not add commentary.";

    private readonly IQwenVisionService _qwen;

    /// <summary>
    /// Creates a new printed-text fallback recognizer.
    /// </summary>
    /// <param name="qwen">The vision inference service. Not null.</param>
    /// <exception cref="ArgumentNullException">qwen is null.</exception>
    public QwenPrintedFallbackRecognizer(IQwenVisionService qwen)
    {
        _qwen = qwen ?? throw new ArgumentNullException(nameof(qwen));
    }

    /// <summary>
    /// Runs the printed-text fallback on one crop.
    /// </summary>
    /// <param name="cropBytes">The printed-line crop as image bytes.</param>
    /// <param name="ppocrText">
    /// The PP-OCR result for the same crop. Used only for the length
    /// acceptance rule; never sent to Qwen. Not null.
    /// </param>
    /// <param name="cancellationToken">Cancels the inference.</param>
    /// <returns>
    /// The cleaned Qwen text when accepted (non-empty and strictly longer
    /// than <paramref name="ppocrText"/>). Empty string otherwise. Never null.
    /// </returns>
    /// <exception cref="ArgumentException">cropBytes is empty.</exception>
    /// <exception cref="ArgumentNullException">ppocrText is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation signaled.</exception>
    /// <exception cref="InvalidOperationException">Qwen inference failed.</exception>
    public async Task<string> RecognizeAsync(
        ReadOnlyMemory<byte> cropBytes,
        string ppocrText,
        CancellationToken cancellationToken = default)
    {
        // Fast-fail on an already-canceled token before the
        // empty-input checks. Avoids a needless Qwen inference
        // (~35 s on CPU) when the caller has already canceled.
        cancellationToken.ThrowIfCancellationRequested();

        if (cropBytes.Length == 0)
            throw new ArgumentException("Crop bytes are empty.", nameof(cropBytes));
        ArgumentNullException.ThrowIfNull(ppocrText);

        var rawOutput = await _qwen.GenerateAsync(
            cropBytes, PrintedPrompt, maxTokens: 256, cancellationToken)
            .ConfigureAwait(false);

        var cleaned = CleanQwenOutput(rawOutput);
        if (cleaned.Length == 0) return string.Empty;
        if (cleaned.Length <= ppocrText.Length) return string.Empty;
        return cleaned;
    }

    private static string CleanQwenOutput(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        var result = raw;
        result = result.Replace("<|im_end|>", string.Empty);
        result = result.Replace("<|endoftext|>", string.Empty);
        result = result.Replace("<|im_start|>", string.Empty);
        return result.Trim();
    }
}
