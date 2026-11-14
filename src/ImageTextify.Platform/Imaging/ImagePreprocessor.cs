// ============================================================================
// FILE:     src/ImageTextify.Platform/Imaging/ImagePreprocessor.cs
// PURPOSE:  The preprocessing orchestrator. Decodes the image stream into a
//           bitmap, produces the Original variant as an unmodified copy, runs
//           the seven preprocessing stages in a fixed order, produces the
//           Enhanced variant, and returns both along with diagnostics.
//           Never mutates the source bitmap; every stage returns a new one.
// RELATION: Implements IImagePreprocessor. Consumed by the OCR engine in
//           Phase 2.4b. The 2.4b caller owns the resilience policy around
//           the whole extraction operation. Preprocessing is deterministic
//           and cancellation-safe.
// ============================================================================

using ImageTextify.Platform.Imaging.Preprocess;
using SkiaSharp;

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// The default <see cref="IImagePreprocessor"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stage order.</b> Decode, orientation, deskew, denoise, contrast,
/// upscale, sharpen (optional), binarize (optional). The order is fixed and
/// not caller-configurable.
/// </para>
/// <para>
/// <b>Variant ownership.</b> The caller owns both returned handles and must
/// dispose them. Intermediate bitmaps are disposed as the pipeline advances.
/// </para>
/// <para>
/// <b>Cancellation.</b> The token is checked before each stage. On
/// cancellation the pipeline throws <see cref="OperationCanceledException"/>
/// and disposes every bitmap it allocated.
/// </para>
/// <para>
/// <b>Resilience.</b> The preprocessor does not use Portfolio.Resilience
/// directly. The Phase 2.4b caller wraps stream read, preprocessing, and
/// inference in the image-textify-ocr policy.
/// </para>
/// </remarks>
public sealed class ImagePreprocessor : IImagePreprocessor
{
    /// <inheritdoc/>
    public async Task<ImagePreprocessingResult> PreprocessAsync(
        Stream imageStream,
        ImagePreprocessingOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(imageStream);
        ArgumentNullException.ThrowIfNull(options);

        cancellationToken.ThrowIfCancellationRequested();

        // Decode. The decoded bitmap is the source of truth for the Original
        // variant and is never mutated. Every stage returns a new bitmap.
        SKBitmap decoded;
        try
        {
            decoded = SKBitmap.Decode(imageStream);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "The image stream could not be decoded.", ex);
        }

        if (decoded is null)
        {
            throw new InvalidOperationException(
                "The image stream could not be decoded.");
        }

        var originalHandle = new SkiaBitmapHandle(CopyOf(decoded));
        var current = decoded;
        var ownsCurrent = true;
        var diagnostics = new ImagePreprocessingDiagnostics(
            OrientationApplied: false,
            DeskewAngleDegrees: null,
            DenoiseApplied: false,
            ContrastApplied: false,
            EffectiveDpi: ComputeEffectiveDpi(decoded, options),
            UpscaleOutcome: "UpscaleSkipped",
            UpscaleFactor: 1.0,
            SharpenApplied: false,
            BinarizeApplied: false);

        var originalWidth = decoded.Width;
        var originalHeight = decoded.Height;

        try
        {
            // Stage 1: orientation
            var orientation = new Orientation();
            var next = await ApplyStageAsync(
                orientation, current, options, cancellationToken).ConfigureAwait(false);
            DisposeIfOwned(ref current, ref ownsCurrent);
            current = next;
            ownsCurrent = true;
            diagnostics = diagnostics with
            {
                OrientationApplied =
                    current.Width != originalWidth ||
                    current.Height != originalHeight,
            };

            // Stage 2: deskew
            var deskew = new Deskew();
            next = await ApplyStageAsync(
                deskew, current, options, cancellationToken).ConfigureAwait(false);
            DisposeIfOwned(ref current, ref ownsCurrent);
            current = next;
            ownsCurrent = true;

            // Stage 3: denoise
            var denoise = new Denoise();
            next = await ApplyStageAsync(
                denoise, current, options, cancellationToken).ConfigureAwait(false);
            DisposeIfOwned(ref current, ref ownsCurrent);
            current = next;
            ownsCurrent = true;
            diagnostics = diagnostics with { DenoiseApplied = true };

            // Stage 4: contrast
            var contrast = new Contrast();
            next = await ApplyStageAsync(
                contrast, current, options, cancellationToken).ConfigureAwait(false);
            DisposeIfOwned(ref current, ref ownsCurrent);
            current = next;
            ownsCurrent = true;
            diagnostics = diagnostics with { ContrastApplied = true };

            // Stage 5: upscale
            var upscale = new Upscale();
            next = await ApplyStageAsync(
                upscale, current, options, cancellationToken).ConfigureAwait(false);
            if (next.Width != current.Width || next.Height != current.Height)
            {
                diagnostics = diagnostics with
                {
                    UpscaleOutcome = "UpscaleApplied",
                    UpscaleFactor = (double)next.Width / current.Width,
                };
            }
            DisposeIfOwned(ref current, ref ownsCurrent);
            current = next;
            ownsCurrent = true;

            // Stage 6: sharpen, optional
            if (options.Sharpen)
            {
                var sharpen = new Sharpen();
                next = await ApplyStageAsync(
                    sharpen, current, options, cancellationToken).ConfigureAwait(false);
                DisposeIfOwned(ref current, ref ownsCurrent);
                current = next;
                ownsCurrent = true;
                diagnostics = diagnostics with { SharpenApplied = true };
            }

            // Stage 7: binarize, optional
            if (options.Binarize)
            {
                var binarize = new Binarize();
                next = await ApplyStageAsync(
                    binarize, current, options, cancellationToken).ConfigureAwait(false);
                DisposeIfOwned(ref current, ref ownsCurrent);
                current = next;
                ownsCurrent = true;
                diagnostics = diagnostics with { BinarizeApplied = true };
            }

            // The Enhanced variant is the final current bitmap. Transfer
            // ownership to the result and stop owning it here.
            var enhancedHandle = new SkiaBitmapHandle(current);
            ownsCurrent = false;

            return new ImagePreprocessingResult(
                ImageVariant.Original,
                ImageVariant.Enhanced,
                originalHandle,
                enhancedHandle,
                diagnostics);
        }
        catch (OperationCanceledException)
        {
            DisposeIfOwned(ref current, ref ownsCurrent);
            originalHandle.Dispose();
            throw;
        }
        catch
        {
            DisposeIfOwned(ref current, ref ownsCurrent);
            originalHandle.Dispose();
            throw;
        }
    }

    private static async Task<SKBitmap> ApplyStageAsync(
        IImageStage stage,
        SKBitmap source,
        ImagePreprocessingOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await stage.ApplyAsync(source, options, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void DisposeIfOwned(ref SKBitmap bitmap, ref bool owns)
    {
        if (owns && bitmap is not null)
        {
            bitmap.Dispose();
        }
        owns = false;
    }

    private static SKBitmap CopyOf(SKBitmap source)
    {
        var copy = new SKBitmap(source.Width, source.Height, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(copy);
        canvas.DrawBitmap(source, 0, 0);
        return copy;
    }

    private static int ComputeEffectiveDpi(SKBitmap bitmap, ImagePreprocessingOptions options)
    {
        if (options.EffectiveDpiHint is { } hint) return hint;
        return bitmap.Width > 1000 ? 200 : 150;
    }
}
