// ============================================================================
// FILE:     src/ImageTextify.Platform/Imaging/SkiaBitmapHandle.cs
// PURPOSE:  A disposable wrapper around SKBitmap so callers cannot forget
//           to release the native resources. The caller owns the handle and
//           must dispose it.
// RELATION: Returned in ImagePreprocessingResult. Consumed by the OCR engine.
// ============================================================================

using SkiaSharp;

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// A disposable owner of an <see cref="SKBitmap"/>.
/// </summary>
/// <remarks>
/// SKBitmap holds native memory and is disposable. The handle exists so the
/// caller cannot leak the bitmap by forgetting a using block. Disposing the
/// handle disposes the underlying bitmap exactly once.
/// </remarks>
public sealed class SkiaBitmapHandle : IDisposable
{
    private SKBitmap? _bitmap;
    private bool _disposed;

    /// <summary>
    /// Wraps an existing bitmap.
    /// </summary>
    /// <param name="bitmap">The bitmap to own. Not null.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="bitmap"/> is null.
    /// </exception>
    public SkiaBitmapHandle(SKBitmap bitmap)
    {
        _bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
    }

    /// <summary>
    /// The wrapped bitmap.
    /// </summary>
    /// <exception cref="ObjectDisposedException">
    /// Thrown when the handle has been disposed.
    /// </exception>
    public SKBitmap Bitmap =>
        _disposed || _bitmap is null
            ? throw new ObjectDisposedException(nameof(SkiaBitmapHandle))
            : _bitmap;

    /// <summary>
    /// The width of the wrapped bitmap in pixels.
    /// </summary>
    public int Width => Bitmap.Width;

    /// <summary>
    /// The height of the wrapped bitmap in pixels.
    /// </summary>
    public int Height => Bitmap.Height;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bitmap?.Dispose();
        _bitmap = null;
    }
}