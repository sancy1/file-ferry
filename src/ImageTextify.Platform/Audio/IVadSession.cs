// filepath: src/ImageTextify.Platform/Audio/IVadSession.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Audio
// since:    Phase 10 Block 10.B.3a
// purpose:  Internal seam over Whisper.net sealed WhisperVadProcessor so
//           the streaming segmenter can be tested without a live VAD
//           context. Whisper.net 1.9.1 declares WhisperVadProcessor sealed
//           (measured 2026-11-11T03:05:13), so it cannot be mocked; this
//           interface is the substitution point the tests inject.
// RELATIONSHIPS
//   Implements : n/a (interface)
//   Depends on : VadSegmentData (Whisper.net, sealed)
//   Used by    : StreamingSileroSegmenter, WhisperVadAdapter,
//                FileHarvester.Tests.ImageTextify.StreamingSileroSegmenterTests
//   See also   : ImageTextify.Platform.Audio.WhisperVadAdapter
// NOTES
//   - Internal. Visible to FileHarvester.Tests via the existing
//     InternalsVisibleTo declaration in ImageTextify.Platform.csproj.
//   - Mirrors exactly the two members StreamingSileroSegmenter uses from
//     WhisperVadProcessor: DetectSpeechNoResetAsync and ResetState.
//   - Not a public contract. Tests are the only intended consumer.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;

namespace ImageTextify.Platform.Audio;

internal interface IVadSession : IDisposable
{
    Task<IReadOnlyList<VadSegmentData>> DetectSpeechNoResetAsync(
        ReadOnlyMemory<float> samples,
        CancellationToken cancellationToken);

    void ResetState();

    /// <summary>
    /// Runs the VAD over a complete WAV stream in one call. The processor
    /// resets its state before this call and returns all speech regions
    /// in the stream.
    /// </summary>
    /// <param name="waveStream">The WAV stream. Not null.</param>
    /// <param name="cancellationToken">Cancels the detection.</param>
    /// <returns>The speech regions, ordered by Start.</returns>
    Task<IReadOnlyList<VadSegmentData>> DetectSpeechAsync(Stream waveStream, CancellationToken cancellationToken);
}