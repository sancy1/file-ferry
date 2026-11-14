// ============================================================================
// filepath: src/ImageTextify.Platform/Imaging/FrameImageWriter.cs
// layer:    Platform / Infrastructure
// module:   ImageTextify.Platform.Imaging
// since:    Phase 9 (Camera Capture)
// purpose:  Persists a captured PNG frame to a user-chosen path atomically.
//           The camera session already produces PNG bytes; this writer only
//           writes them to disk via temp-file + File.Move.
// RELATIONSHIPS
//   Implements : n/a (utility)
//   Depends on : n/a (BCL only)
//   Used by    : FileHarvester.App.ViewModels.CameraCaptureViewModel (via a
//                delegate passed from MainWindowViewModel)
//   See also   : PeripheryCameraCaptureSession.cs (produces the PNG stream);
//                Prompt.Platform.Export.PromptExporter (same temp-rename pattern)
// NOTES
//   - The caller owns the stream. This writer does NOT dispose it.
//   - The write is atomic: temp file in the destination directory, then
//     File.Move(temp, dest, overwrite: true). A failed or cancelled write
//     never leaves a partial destination file.
//   - The PNG encoder lives in PeripheryCameraCaptureSession, not here: the
//     session already delivers PNG. Re-encoding would be redundant.
// ============================================================================

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// Persists a captured PNG frame stream to a user-chosen path atomically.
/// </summary>
public static class FrameImageWriter
{
    /// <summary>
    /// Writes the PNG bytes from <paramref name="pngStream"/> to
    /// <paramref name="destinationPath"/> using a temp-file + rename.
    /// The caller owns the stream.
    /// </summary>
    /// <param name="pngStream">The PNG bytes. Not null; must be readable.</param>
    /// <param name="destinationPath">The target file path. Not null or empty.</param>
    /// <param name="cancellationToken">Cancels the write; no partial destination is left.</param>
    public static async Task WriteAsync(
        Stream pngStream,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pngStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var tempPath = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var dest = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                await pngStream.CopyToAsync(dest, 64 * 1024, cancellationToken).ConfigureAwait(false);
                await dest.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }
}
