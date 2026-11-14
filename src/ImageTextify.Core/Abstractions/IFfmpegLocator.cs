// filepath: src/ImageTextify.Core/Abstractions/IFfmpegLocator.cs
// layer:    Contracts
// module:   ImageTextify.Core.Abstractions
// since:    Phase 5 Block 5.B.1
// purpose:  Contract for locating the pinned FFmpeg and ffprobe binaries.
//           The app never resolves FFmpeg from PATH; the locator is the
//           only way to obtain the binary paths, and it fails loudly if
//           the pinned binaries are not present.
//
// RELATIONSHIPS
//   Implements : n/a (interface)
//   Depends on : n/a (BCL only)
//   Used by    : ImageTextify.Platform.Media.FfprobeProcessAdapter,
//                ImageTextify.Platform.Media.FfmpegDecodeAdapter
//   See also   : ImageTextify.Core.Abstractions.IMediaProbe,
//                ImageTextify.Core.Abstractions.IAudioDecoder
//
// NOTES
//   - The locator never consults PATH. A user with FFmpeg installed
//     globally is not a dependency. This is Decision 1 of the technology
//     decision document.
//   - A missing binary is an exception, not a false return. The pipeline
//     maps the exception to TranscriptionOutcome.ToolMissing.

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// Locates the pinned FFmpeg and ffprobe binaries.
/// </summary>
public interface IFfmpegLocator
{
    /// <summary>
    /// Returns the absolute path to the pinned ffmpeg.exe.
    /// </summary>
    /// <returns>The absolute path. Never null.</returns>
    /// <exception cref="FileNotFoundException">
    /// Thrown when the pinned ffmpeg.exe is not present at the configured location.
    /// </exception>
    string GetFfmpegPath();

    /// <summary>
    /// Returns the absolute path to the pinned ffprobe.exe.
    /// </summary>
    /// <returns>The absolute path. Never null.</returns>
    /// <exception cref="FileNotFoundException">
    /// Thrown when the pinned ffprobe.exe is not present at the configured location.
    /// </exception>
    string GetFfprobePath();
}