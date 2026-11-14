// ============================================================================
// FILE:     src/ImageTextify.Core/Abstractions/IAudioEnhancer.cs
// PURPOSE:  Contract for conservative speech-oriented preprocessing.
//           Reduces steady background noise where safe and normalizes
//           the signal before transcription. Makes no claim of clean
//           voice separation.
// RELATION: Implemented in the Live Conversation Transcription phase.
//           Consumed by the live transcription pipeline and, where
//           useful, by the file audio pipeline.
// ============================================================================

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// Applies conservative audio enhancement before transcription.
/// </summary>
/// <remarks>
/// <para>
/// Enhancement improves recognition accuracy on steady background
/// noise, echo, and variable input levels. It is not voice separation
/// and it does not restore speech that was never captured. The
/// implementation must not claim to remove all background noise or
/// to isolate individual speakers.
/// </para>
/// <para>
/// The caller owns the input stream. The enhancer returns a new stream
/// that the caller owns and disposes.
/// </para>
/// </remarks>
public interface IAudioEnhancer
{
    /// <summary>
    /// Enhances raw audio for transcription.
    /// </summary>
    /// <param name="rawAudioStream">
    /// The raw audio bytes. The caller owns the stream; the enhancer
    /// must not close it.
    /// </param>
    /// <param name="options">
    /// Enhancement options. Not null.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the enhancement. On cancellation no partial output is
    /// left behind.
    /// </param>
    /// <returns>
    /// A new readable stream of enhanced audio. The caller owns and
    /// closes it.
    /// </returns>
    Task<Stream> EnhanceAsync(
        Stream rawAudioStream,
        AudioEnhancementOptions options,
        CancellationToken cancellationToken);
}

/// <summary>
/// Options controlling audio enhancement.
/// </summary>
/// <param name="NoiseSuppression">
/// When true, reduce steady background noise. Default true.
/// </param>
/// <param name="EchoCancellation">
/// When true, reduce speaker or loopback echo where the platform
/// supports it. Default true.
/// </param>
/// <param name="AutomaticGainControl">
/// When true, normalize quiet or variable microphone levels. Default
/// true.
/// </param>
/// <param name="VoiceActivityDetection">
/// When true, mark speech segments so the transcriber can skip silence
/// and non-speech noise. Default true.
/// </param>
public sealed record AudioEnhancementOptions(
    bool NoiseSuppression = true,
    bool EchoCancellation = true,
    bool AutomaticGainControl = true,
    bool VoiceActivityDetection = true);