// filepath: src/ImageTextify.Platform/Imaging/QwenHandwritingRecognizer.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Imaging
// since:    Phase 2.4d
// purpose:  Handwriting-specific wrapper around IQwenVisionService.
//
// RELATIONSHIPS
//   Implements : n/a (concrete class)
//   Depends on : IQwenVisionService
//   Used by    : ImageTextify.Platform.OnnxOcrEngine (Phase 2.4d wiring)
//   See also   : QwenVisionService.cs, IQwenVisionService.cs
//
// NOTES
//   - No separate handwriting classifier. OcrLineRouter determines routing.
//   - Does not rewrite, summarize, translate, or correct wording.
//   - Returns empty string when Qwen produces empty output.
//   - Not thread-safe for concurrent calls. Delegates to IQwenVisionService
//     which serializes with a SemaphoreSlim.
//   - Performance: inherits from IQwenVisionService (~35 seconds per call).

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// Handwriting-specific wrapper around <see cref="IQwenVisionService"/>.
/// </summary>
/// <remarks>
/// Sends a handwriting crop to Qwen with a handwriting-specific prompt,
/// strips trailing chat tokens and outer whitespace, returns plain text.
/// Does not rewrite or correct the model output.
/// </remarks>
public sealed class QwenHandwritingRecognizer
{
    /// <summary>
    /// The prompt sent to Qwen for handwriting recognition.
    /// </summary>
    internal const string HandwritingPrompt =
        "Read the handwriting in this image. Return only the text you see. " +
        "Do not invent characters. Do not add commentary.";

    private readonly IQwenVisionService _qwen;

    /// <summary>
    /// Creates a new handwriting recognizer.
    /// </summary>
    /// <param name="qwen">The vision inference service. Not null.</param>
    /// <exception cref="ArgumentNullException">qwen is null.</exception>
    public QwenHandwritingRecognizer(IQwenVisionService qwen)
    {
        _qwen = qwen ?? throw new ArgumentNullException(nameof(qwen));
    }

    /// <summary>
    /// Recognizes handwriting in the given crop.
    /// </summary>
    /// <param name="cropBytes">The handwriting crop as image bytes.</param>
    /// <param name="cancellationToken">Cancels the inference.</param>
    /// <returns>Cleaned text. Never null. Empty when no text.</returns>
    /// <exception cref="ArgumentException">cropBytes is empty.</exception>
    /// <exception cref="OperationCanceledException">Cancellation signaled.</exception>
    /// <exception cref="InvalidOperationException">Qwen inference failed.</exception>
    public async Task<string> RecognizeAsync(
        ReadOnlyMemory<byte> cropBytes,
        CancellationToken cancellationToken = default)
    {
        if (cropBytes.Length == 0)
            throw new ArgumentException("Crop bytes are empty.", nameof(cropBytes));

        var rawOutput = await _qwen.GenerateAsync(
            cropBytes, HandwritingPrompt, maxTokens: 256, cancellationToken)
            .ConfigureAwait(false);

        return CleanQwenOutput(rawOutput);
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

