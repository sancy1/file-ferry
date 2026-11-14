// filepath: src/ImageTextify.Platform/Imaging/IQwenVisionService.cs
// layer:    Infrastructure (interface)
// module:   ImageTextify.Platform.Imaging
// since:    Phase 2.4d
// purpose:  Prompt-agnostic vision inference contract. Takes image bytes and
//           a prompt string, returns generated text. Consumers supply their
//           own prompt. The service knows nothing about handwriting or OCR.
//
// RELATIONSHIPS
//   Implements : n/a (interface)
//   Depends on : n/a (no external types at the interface level)
//   Used by    : ImageTextify.Platform.QwenHandwritingRecognizer
//                ImageTextify.Platform.QwenPrintedFallbackRecognizer (Phase 2.4e)
//                Future: PdfLayoutRecognizer, external consumers via DI
//   See also   : QwenVisionService.cs, OnnxModelRunner.cs
//
// NOTES
//   - The model (Qwen2.5-VL-3B-Instruct) is loaded once as a singleton
//     when this service is first resolved. Subsequent calls reuse the
//     loaded model. Loading is expensive (~30-60 seconds); repeated loads
//     would be unusable.
//   - The service is prompt-agnostic by design. The handwriting consumer
//     supplies a handwriting prompt. The printed fallback consumer supplies
//     a printed-text prompt. Future consumers supply their own. This makes
//     the same model reusable across handwriting, printed fallback, PDF
//     layout, and any future vision task without re-bundling.
//   - Not thread-safe per call: the underlying LLamaSharp engine is not
//     thread-safe for concurrent inference. Callers must serialize calls,
//     or wrap concurrent calls with a SemaphoreSlim at the consumer level.
//     The OnnxOcrEngine processes lines sequentially, so this is not a
//     constraint in the current pipeline.
//   - Cancellation is honored at every await point. The underlying
//     LLamaSharp inference loop checks the token per generated token.
//   - Returns empty string if the model produces no text (rare but
//     possible on unreadable images). Callers must handle the empty case
//     explicitly. Never returns null.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// Prompt-agnostic vision inference service. Runs an image through a
/// vision-language model with a caller-supplied prompt and returns the
/// generated text.
/// </summary>
/// <remarks>
/// <para>
/// The service knows nothing about handwriting, printed text, or any
/// specific task. Every caller supplies its own prompt. This makes the
/// same underlying model reusable across multiple consumers without
/// re-bundling or re-loading.
/// </para>
/// <para>
/// The service is registered as a singleton. The model is loaded once
/// on first use and reused across all consumers for the lifetime of the
/// application process.
/// </para>
/// <para>
/// Implementations are not thread-safe for concurrent inference. Callers
/// that invoke from multiple threads must serialize calls externally.
/// The current pipeline (OnnxOcrEngine) processes lines sequentially, so
/// this is not a constraint.
/// </para>
/// </remarks>
public interface IQwenVisionService : IAsyncDisposable
{
    /// <summary>
    /// Runs a single image+prompt inference.
    /// </summary>
    /// <param name="imageBytes">
    /// The image as raw bytes (PNG, JPEG, WebP, or any format the
    /// underlying image decoder accepts). Not null, not empty.
    /// </param>
    /// <param name="prompt">
    /// The natural-language instruction for the model. Must be non-null
    /// and non-empty. The prompt determines the task: handwriting
    /// recognition, printed-text extraction, layout parsing, etc.
    /// </param>
    /// <param name="maxTokens">
    /// Maximum number of tokens to generate. Default 256. Larger values
    /// allow longer outputs but increase latency linearly.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the inference. Cancellation surfaces as
    /// <see cref="OperationCanceledException"/>. No partial state is
    /// left behind on cancellation.
    /// </param>
    /// <returns>
    /// The generated text. Never null. Empty string when the model
    /// produced no text (unreadable image, prompt mismatch, or model
    /// failure). Callers must handle the empty case explicitly.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// imageBytes is null, or prompt is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// imageBytes is empty, or prompt is empty or whitespace.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// The cancellation token was signaled.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The underlying model failed to load or produced an inference
    /// error. The implementation wraps native exceptions in this type
    /// so callers can distinguish model failure from cancellation.
    /// </exception>
    Task<string> GenerateAsync(
        ReadOnlyMemory<byte> imageBytes,
        string prompt,
        int maxTokens = 256,
        CancellationToken cancellationToken = default);
}

