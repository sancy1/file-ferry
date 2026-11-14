// filepath: src/ImageTextify.Platform/Audio/WhisperVadAdapter.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Audio
// since:    Phase 10 Block 10.B.3a
// purpose:  Adapter from Whisper.net sealed WhisperVadProcessor to the
//           internal IVadSession seam. Pure pass-through; no logic of its
//           own. Exists only so the streaming segmenter can be tested with
//           a fake VAD (the sealed processor cannot be subclassed).
// RELATIONSHIPS
//   Implements : ImageTextify.Platform.Audio.IVadSession
//   Depends on : Whisper.net (WhisperVadProcessor, VadSegmentData)
//   Used by    : StreamingSileroSegmenter (production path)
//   See also   : ImageTextify.Platform.Audio.IVadSession
// NOTES
//   - Internal. Visible to FileHarvester.Tests via InternalsVisibleTo.
//   - Ownership: this adapter owns the WhisperVadProcessor it is given.
//     Disposing the adapter disposes the processor. The caller must not
//     dispose the processor separately after constructing the adapter.
//   - The WhisperVadFactory that created the processor is the caller's
//     responsibility; it is not held by this adapter.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;

namespace ImageTextify.Platform.Audio;

internal sealed class WhisperVadAdapter : IVadSession
{
    private readonly WhisperVadProcessor _processor;

    public WhisperVadAdapter(WhisperVadProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        _processor = processor;
    }

    public Task<IReadOnlyList<VadSegmentData>> DetectSpeechNoResetAsync(
        ReadOnlyMemory<float> samples,
        CancellationToken cancellationToken)
        => _processor.DetectSpeechNoResetAsync(samples, cancellationToken);

    public Task<IReadOnlyList<VadSegmentData>> DetectSpeechAsync(Stream waveStream, CancellationToken cancellationToken)
        => _processor.DetectSpeechAsync(waveStream, cancellationToken);

    public void ResetState() => _processor.ResetState();

    public void Dispose() => _processor.Dispose();
}