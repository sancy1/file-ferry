// filepath: src/ImageTextify.Platform/Media/ConfiguredFfmpegLocator.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Media
// since:    Phase 5 Block 5.B.1
// purpose:  Adapter that resolves the pinned FFmpeg and ffprobe binaries
//           from a caller-supplied base directory. Fails loudly if either
//           binary is absent. Never consults PATH.
//
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.IFfmpegLocator
//   Depends on : System.IO (BCL only)
//   Used by    : ImageTextify.Platform.Media.FfprobeProcessAdapter,
//                ImageTextify.Platform.Media.FfmpegDecodeAdapter
//   See also   : ImageTextify.Core.Abstractions.IFfmpegLocator
//
// NOTES
//   - Windows-only for the first release. The filenames are "ffmpeg.exe"
//     and "ffprobe.exe". Cross-platform resolution is a future concern.
//   - The base directory is expected to be the "bin" folder of the
//     extracted BtbN LGPL build, as recorded in the technology decision
//     document (F-AV-012).

using System;
using System.IO;
using ImageTextify.Core.Abstractions;

namespace ImageTextify.Platform.Media;

/// <summary>
/// Resolves FFmpeg and ffprobe from a configured bin directory.
/// </summary>
public sealed class ConfiguredFfmpegLocator : IFfmpegLocator
{
    private const string FfmpegExeName = "ffmpeg.exe";
    private const string FfprobeExeName = "ffprobe.exe";

    private readonly string _baseDirectory;

    /// <summary>
    /// Constructs the locator.
    /// </summary>
    /// <param name="baseDirectory">
    /// The directory containing ffmpeg.exe and ffprobe.exe. Not null; must exist.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when baseDirectory is null.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when baseDirectory does not exist.</exception>
    public ConfiguredFfmpegLocator(string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(baseDirectory);
        if (!Directory.Exists(baseDirectory))
        {
            throw new DirectoryNotFoundException(
                "The FFmpeg directory does not exist: " + baseDirectory);
        }
        _baseDirectory = baseDirectory;
    }

    /// <inheritdoc/>
    /// <exception cref="FileNotFoundException">Thrown when ffmpeg.exe is missing.</exception>
    public string GetFfmpegPath()
    {
        var path = Path.Combine(_baseDirectory, FfmpegExeName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "The pinned ffmpeg binary was not found at: " + path, path);
        }
        return path;
    }

    /// <inheritdoc/>
    /// <exception cref="FileNotFoundException">Thrown when ffprobe.exe is missing.</exception>
    public string GetFfprobePath()
    {
        var path = Path.Combine(_baseDirectory, FfprobeExeName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "The pinned ffprobe binary was not found at: " + path, path);
        }
        return path;
    }
}