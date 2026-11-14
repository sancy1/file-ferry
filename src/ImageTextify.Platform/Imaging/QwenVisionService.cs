// filepath: src/ImageTextify.Platform/Imaging/QwenVisionService.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Imaging
// since:    Phase 2.4d
// purpose:  LLamaSharp implementation of IQwenVisionService. Loads Qwen2.5-VL-3B
//           (GGUF text model + F16 mmproj) once as a singleton and runs
//           image+prompt inference with the Qwen chat template.
//
// RELATIONSHIPS
//   Implements : IQwenVisionService
//   Depends on : LLamaSharp (LLamaWeights, MtmdWeights, InteractiveExecutor)
//                LLama.Common (ModelParams, InferenceParams)
//                LLama.Native (MtmdContextParams, NativeApi)
//   Used by    : ImageTextify.Platform.QwenHandwritingRecognizer (Phase 2.4d)
//                ImageTextify.Platform.QwenPrintedFallbackRecognizer (Phase 2.4e)
//   See also   : IQwenVisionService.cs, OnnxOcrEngine.cs
//
// NOTES
//   - Model is loaded once in the constructor. Loading takes 30-60 seconds on
//     CPU (Q4_K_M text + F16 mmproj, ~3 GB total). Subsequent GenerateAsync
//     calls reuse the loaded model.
//   - Thread safety: GenerateAsync is serialized with a SemaphoreSlim(1, 1).
//     The underlying InteractiveExecutor is not thread-safe.
//   - Performance: ~35 seconds per inference on CPU. The UI must show
//     progress or run on a background thread.
//   - The Qwen chat template is required. A bare prompt produces 4 tokens
//     of garbage because Qwen2.5-VL expects the user/assistant turn format.
//   - The media marker (from mtmdParams.MediaMarker) is prepended inside
//     the user turn. Without it, mtmd_tokenize fails.
//   - No logging. Logging was removed to satisfy CA1848/CA1873 without
//     adding LoggerMessage source-generated delegates. Consuming classes
//     can log if needed.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;
using SkiaSharp;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// LLamaSharp implementation of <see cref="IQwenVisionService"/>.
/// </summary>
/// <remarks>
/// Loads Qwen2.5-VL-3B-Instruct (GGUF Q4_K_M text + F16 mmproj) once
/// in the constructor and reuses the model for all GenerateAsync calls.
/// Inference is serialized. Expected throughput: ~35 seconds per call
/// on modern CPU.
/// </remarks>
public sealed class QwenVisionService : IQwenVisionService
{
    private readonly LLamaWeights _weights;
    private readonly MtmdWeights _mtmd;
    private readonly ModelParams _modelParams;
    private readonly string _mediaMarker;
    private readonly SemaphoreSlim _inferenceLock;
    private bool _disposed;

    /// <summary>
    /// Creates a new QwenVisionService by loading the text model and
    /// vision projector from disk.
    /// </summary>
    /// <param name="textModelPath">
    /// Path to the Qwen2.5-VL-3B text model (Q4_K_M GGUF, ~1.84 GB).
    /// </param>
    /// <param name="mmprojPath">
    /// Path to the vision projector (F16 GGUF, ~1.28 GB).
    /// </param>
    /// <exception cref="FileNotFoundException">
    /// Either model file is missing.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The models failed to load. The inner exception contains the native
    /// error.
    /// </exception>
    public QwenVisionService(
        string textModelPath,
        string mmprojPath)
    {
        if (!File.Exists(textModelPath))
            throw new FileNotFoundException(
                "Qwen text model not found: " + textModelPath, textModelPath);
        if (!File.Exists(mmprojPath))
            throw new FileNotFoundException(
                "Qwen mmproj not found: " + mmprojPath, mmprojPath);

        _inferenceLock = new SemaphoreSlim(1, 1);
        _modelParams = new ModelParams(textModelPath)
        {
            ContextSize = 4096,
            GpuLayerCount = 0,
        };

        try
        {
            _weights = LLamaWeights.LoadFromFile(_modelParams);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to load Qwen text model from " + textModelPath, ex);
        }

        try
        {
            var mtmdParams = MtmdContextParams.Default();
            mtmdParams.UseGpu = false;
            _mtmd = MtmdWeights.LoadFromFile(mmprojPath, _weights, mtmdParams);
            _mediaMarker = mtmdParams.MediaMarker
                ?? NativeApi.MtmdDefaultMarker()
                ?? "<media>";
        }
        catch (Exception ex)
        {
            _weights.Dispose();
            throw new InvalidOperationException(
                "Failed to load Qwen vision projector from " + mmprojPath, ex);
        }
    }

    /// <inheritdoc/>
    public async Task<string> GenerateAsync(
        ReadOnlyMemory<byte> imageBytes,
        string prompt,
        int maxTokens = 256,
        CancellationToken cancellationToken = default)
    {
        if (imageBytes.Length == 0)
            throw new ArgumentException("Image bytes are empty.", nameof(imageBytes));
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt is null or whitespace.", nameof(prompt));
        if (maxTokens <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTokens), "Must be positive.");
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        await _inferenceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chatPrompt =
                "<|im_start|>user\n"
                + _mediaMarker
                + prompt
                + "<|im_end|>\n<|im_start|>assistant\n";

            // Normalize any image format (WebP, GIF, TIFF, BMP, PNG, JPEG) to PNG
            // before handing it to the llama.cpp vision loader, which supports a
            // narrower set of formats. SKBitmap.Decode handles all SkiaSharp formats;
            // re-encoding as PNG guarantees the loader accepts the bytes.
            byte[] normalizedBytes;
            using (var bitmap = SKBitmap.Decode(imageBytes.ToArray()))
            {
                if (bitmap is null)
                {
                    // Decode failed; pass original bytes through. The loader will
                    // fail with its own specific error, which is more useful than
                    // a generic decode-failed exception here.
                    normalizedBytes = imageBytes.ToArray();
                }
                else
                {
                    using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                    normalizedBytes = data.ToArray();
                }
            }
            _mtmd.LoadMedia(normalizedBytes);

            using var context = _weights.CreateContext(_modelParams);
            var executor = new InteractiveExecutor(context, _mtmd, NullLogger.Instance);

            var inferenceParams = new InferenceParams
            {
                MaxTokens = maxTokens,
                // Greedy sampling: always pick the highest-probability token.
                // Deterministic output. Matches Python benchmark temperature=0.0.
                SamplingPipeline = new LLama.Sampling.GreedySamplingPipeline(),
                AntiPrompts = new[] { "<|im_end|>", "<|endoftext|>" },
            };

            var responseBuilder = new System.Text.StringBuilder();
            await foreach (var token in executor
                .InferAsync(chatPrompt, inferenceParams, cancellationToken)
                .ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                responseBuilder.Append(token);
            }

            return responseBuilder.ToString();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Qwen inference failed. See inner exception for the native error.", ex);
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _mtmd?.Dispose();
        _weights?.Dispose();
        _inferenceLock.Dispose();
        return ValueTask.CompletedTask;
    }
}

