// ============================================================================
// FILE:     src/ImageTextify.Core/Abstractions/IMediaPreparer.cs
// PURPOSE:  Contract for media probing and audio extraction. The
//           default path extracts only the audio track; a full video
//           conversion happens only on an explicit user request.
// RELATION: Implemented in Phases 5 and 6 with FFmpeg. Consumed by the
//           audio and video pipelines.
// ============================================================================

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// Probes media and prepares 16 kHz mono PCM for transcription.
/// </summary>
public interface IMediaPreparer
{
    /// <summary>
    /// Probes the media container and its audio track without decoding
    /// the entire file.
    /// </summary>
    /// <param name="mediaStream">
    /// The media bytes. The caller owns the stream; the preparer must
    /// not close it.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the probe. The preparer honors the token.
    /// </param>
    /// <returns>
    /// The container, codecs, duration, and whether the audio track is
    /// directly decodable.
    /// </returns>
    Task<MediaProbeResult> ProbeAsync(
        Stream mediaStream,
        CancellationToken cancellationToken);

    /// <summary>
    /// Extracts the audio track as 16 kHz mono PCM in WAV.
    /// </summary>
    /// <param name="mediaStream">
    /// The media bytes. The caller owns the stream.
    /// </param>
    /// <param name="options">
    /// Extraction options. Not null.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the extraction. On cancellation no partial file
    /// remains.
    /// </param>
    /// <returns>
    /// A readable stream of 16 kHz mono PCM WAV. The caller owns and
    /// closes the returned stream.
    /// </returns>
    Task<Stream> ExtractAudioAsync(
        Stream mediaStream,
        MediaExtractionOptions options,
        CancellationToken cancellationToken);
}

/// <summary>
/// The result of a media probe.
/// </summary>
/// <param name="Container">
/// The container format, for example <c>mp4</c>.
/// </param>
/// <param name="AudioCodec">
/// The audio codec, or null when no audio track exists.
/// </param>
/// <param name="VideoCodec">
/// The video codec, or null for an audio-only file.
/// </param>
/// <param name="Duration">
/// The duration of the media, or null when unknown.
/// </param>
/// <param name="HasAudioTrack">
/// True when an audio track is present.
/// </param>
/// <param name="AudioDirectlyDecodable">
/// True when the audio track can be decoded without a full container
/// transcode. The default path uses this to skip conversion.
/// </param>
public sealed record MediaProbeResult(
    string Container,
    string? AudioCodec,
    string? VideoCodec,
    TimeSpan? Duration,
    bool HasAudioTrack,
    bool AudioDirectlyDecodable);

/// <summary>
/// Options controlling audio extraction.
/// </summary>
/// <param name="ConvertFullVideoOnExplicitRequest">
/// When false (the default), only the audio track is extracted. When
/// true, the caller has explicitly requested a converted copy, and the
/// preparer may transcode the full container. Never set true without
/// an explicit user request.
/// </param>
public sealed record MediaExtractionOptions(
    bool ConvertFullVideoOnExplicitRequest = false);