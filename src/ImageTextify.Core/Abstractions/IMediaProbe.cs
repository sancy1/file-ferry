// filepath: src/ImageTextify.Core/Abstractions/IMediaProbe.cs
// layer:    Contracts
// module:   ImageTextify.Core.Abstractions
// since:    Phase 5 Block 5.A.2
// purpose:  Contract for probing a media container: container name, audio
//           tracks, per-track codec, duration, channels, and sample rate.
//           The probe reads metadata only; decoding happens in IAudioDecoder.
//
// RELATIONSHIPS
//   Implements : n/a (interface)
//   Depends on : n/a (BCL only)
//   Used by    : ImageTextify.Platform.Speech.TranscriptionPipeline,
//                ImageTextify.Core.Abstractions.IAudioDecoder
//   See also   : ImageTextify.Core.Abstractions.ITranscriptionPipeline
//
// NOTES
//   - The probe is the first stage of the pipeline. A failure here maps to
//     TranscriptionOutcome.UnsupportedMedia or NoAudioTrack.
//   - IsSupported is false for containers or codecs the pipeline cannot
//     decode; UnsupportedReason is a user-facing explanation in that case.

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// Probes a media container for the information the pipeline needs.
/// </summary>
public interface IMediaProbe
{
    /// <summary>
    /// Probes a file.
    /// </summary>
    /// <param name="filePath">The absolute file path. Not null.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>The probe result. Never null.</returns>
    Task<AudioProbeResult> ProbeAsync(string filePath, CancellationToken cancellationToken);
}

/// <summary>
/// The result of a probe.
/// </summary>
/// <param name="Container">The container name (mp4, mkv, wav, etc.).</param>
/// <param name="Duration">The media duration.</param>
/// <param name="AudioStreams">The audio tracks. Never null; may be empty.</param>
/// <param name="IsSupported">True when the pipeline can decode this container and codec set.</param>
/// <param name="UnsupportedReason">A user-facing reason when IsSupported is false, else null.</param>
public sealed record AudioProbeResult(
    string Container,
    TimeSpan Duration,
    IReadOnlyList<AudioStreamInfo> AudioStreams,
    bool IsSupported,
    string? UnsupportedReason);

/// <summary>
/// One audio stream within a container.
/// </summary>
/// <param name="Index">The stream index within the container.</param>
/// <param name="Codec">The codec name (aac, opus, mp3, etc.).</param>
/// <param name="Channels">The channel count. Positive.</param>
/// <param name="SampleRateHz">The sample rate in Hz. Positive.</param>
/// <param name="Language">The language tag if the container reports one, else null.</param>
public sealed record AudioStreamInfo(
    int Index,
    string Codec,
    int Channels,
    int SampleRateHz,
    string? Language);