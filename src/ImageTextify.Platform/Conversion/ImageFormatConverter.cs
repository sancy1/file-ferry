// ============================================================================
// filepath: src/ImageTextify.Platform/Conversion/ImageFormatConverter.cs
// layer:    Platform / Infrastructure
// module:   ImageTextify.Platform.Conversion
// since:    Phase 11 (Document/Image Conversion)
// purpose:  Converts an image file between PNG, JPG, JPEG, WEBP, ICO, and GIF.
//           Every pair is supported (Meziantou.Framework.Imaging detects the
//           source format from content, not extension).
//
// RELATIONSHIPS
//   Implements : n/a (per-format converter; called by ConversionService)
//   Depends on : Meziantou.Framework.Imaging (Image, encoders)
//   Used by    : ConversionService
//   See also   : Phase 11 Spike 1 (all six formats round-tripped cleanly)
//
// NOTES
//   - Every encoder passes MetadataHandling.DiscardUnsupported. Formats that
//     cannot store the source resolution metadata (WebP/ICO/GIF) would throw
//     UnsupportedImageFeatureException otherwise. Proven in Spike 1.
//   - The write is atomic: write to a temp path, then File.Move(overwrite).
//     On failure or cancellation, no file is left at the destination path.
//   - Supported targets: Png, Jpg, Jpeg, Webp, Ico, Gif. Any other target is
//     rejected by ConversionService before this converter is called.
// ============================================================================

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Formats;

namespace ImageTextify.Platform.Conversion;

/// <summary>
/// Image-format-to-image-format converter over Meziantou.Framework.Imaging.
/// </summary>
internal static class ImageFormatConverter
{
    /// <summary>ICO canvases store at most 256 pixels per side.</summary>
    private const int IcoMaxDimension = 256;

    /// <summary>
    /// Scales (width, height) down proportionally so both fit within
    /// <paramref name="max"/>. Never upscales.
    /// </summary>
    private static (int Width, int Height) FitWithin(int width, int height, int max)
    {
        var scale = Math.Min(1.0, Math.Min((double)max / width, (double)max / height));
        return (
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }

    /// <summary>
    /// Converts <paramref name="inputPath"/> to <paramref name="target"/> at
    /// <paramref name="outputPath"/>. Atomic: no partial file is left behind.
    /// </summary>
    /// <param name="inputPath">The source image path. Not null.</param>
    /// <param name="outputPath">The destination path. Not null.</param>
    /// <param name="target">The target image format.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>A success result with the output path, or a failure with a reason.</returns>
    public static async Task<ConversionResult> ConvertAsync(
        string inputPath,
        string outputPath,
        ConversionFormat target,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath))
            return ConversionResult.Fail("Input file not found: " + inputPath);

        var tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await Task.Run(() =>
            {
                using var loaded = Image.Load(inputPath);
                if (target == ConversionFormat.Ico)
                {
                    if (loaded.Width > IcoMaxDimension || loaded.Height > IcoMaxDimension)
                    {
                        var (w, h) = FitWithin(loaded.Width, loaded.Height, IcoMaxDimension);
                        ImageProcessingExtensions.Resize(loaded, new ResizeOptions(w, h), cancellationToken);
                    }
                    // DIB payloads store 8-bit samples only; normalize so a
                    // 16-bit source (e.g. 16-bit PNG) does not get rejected.
                    using var rgba = loaded.CloneAs<Rgba32>();
                    SaveWith(rgba, tempPath, target);
                }
                else
                {
                    SaveWith(loaded, tempPath, target);
                }
            }, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, outputPath, overwrite: true);
            return ConversionResult.Ok(outputPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ConversionResult.Fail("Image conversion failed: " + ex.Message);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    // WHAT: dispatch to the correct concrete encoder for the target format.
    // WHY:  Meziantou Save takes a concrete encoder type; MetadataHandling must
    //       be DiscardUnsupported for formats that cannot store resolution metadata.
    private static void SaveWith(Image image, string outPath, ConversionFormat target)
    {
        switch (target)
        {
            case ConversionFormat.Png: image.Save(outPath, new PngEncoder()); break;
            case ConversionFormat.Jpg:
            case ConversionFormat.Jpeg: image.Save(outPath, new JpegEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported }); break;
            case ConversionFormat.Webp: image.Save(outPath, new WebPEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported }); break;
            case ConversionFormat.Ico: image.Save(outPath, new IcoEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported, PayloadFormat = IconPayloadFormat.Dib }); break;
            case ConversionFormat.Gif: image.Save(outPath, new GifEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported }); break;
            default: throw new NotSupportedException("Image target format not supported: " + target);
        }
    }
}
