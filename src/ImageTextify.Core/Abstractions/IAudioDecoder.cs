// filepath: src/ImageTextify.Core/Abstractions/IAudioDecoder.cs
// layer:    Contracts
// module:   ImageTextify.Core.Abstractions
// since:    Phase 5 Block 5.A.2
// purpose:  Contract for decoding one audio stream from a media container
//           into 16 kHz mono 16-bit PCM in a temporary WAV. The decoder
//           writes only inside the caller-supplied temp directory.
//
// RELATIONSHIPS
//   Implements : n/a (interface)
//   Depends on : n/a (BCL only)
//   Used by    : ImageTextify.Platform.Speech.TranscriptionPipeline
//   See also   : ImageTextify.Core.Abstractions.IMediaProbe
//
// NOTES
//   - The output sample rate and channel count are fixed at 16 kHz mono
//     because Whisper requires it.
//   - The decoder never writes next to the input file. Every temp path
//     lives under the caller-supplied output directory and is removed by
//     the caller on every exit path.
//   - A missing decoder executable maps to TranscriptionOutcome.ToolMissing
//     at the pipeline layer.

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// Decodes an audio stream from a container to PCM.
/// </summary>
public interface IAudioDecoder
{
    /// <summary>
    /// Decodes the chosen stream to a 16 kHz mono WAV in <paramref name="outputDirectory"/>.
    /// </summary>
    /// <param name="filePath">The source file. Not null.</param>
    /// <param name="streamIndex">The stream index to decode. Not null.</param>
    /// <param name="outputDirectory">The temp directory to write into. Not null, must exist.</param>
    /// <param name="progress">The progress reporter, may be null.</param>
    /// <param name="cancellationToken">Cancels the decode; the partial output is removed.</param>
    /// <returns>The absolute path of the decoded WAV. The caller owns deletion.</returns>
    Task<string> DecodeAsync(
        string filePath,
        int streamIndex,
        string outputDirectory,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken);
}