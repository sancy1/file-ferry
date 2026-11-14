// filepath: src/ImageTextify.Core/Abstractions/ILiveRecordingService.cs
// layer:    Contracts
// module:   ImageTextify.Core.Abstractions
// since:    Phase 10 Block 10.F.1
// purpose:  Contract for opt-in live audio recording. The live transcription
//           path can record the captured audio to a WAV file while it
//           transcribes. Recording is a separate user action; transcription
//           never implies recording.
// RELATIONSHIPS
//   Implements : n/a (interface)
//   Depends on : n/a (BCL only)
//   Used by    : ImageTextify.Platform.Audio.LiveRecordingService (implementation),
//                ImageTextify.Platform.Audio.TeeStream (sink),
//                FileHarvester.App.ViewModels.LiveTranscriptionViewModel
//   See also   : ImageTextify.Core.Abstractions.IAudioCaptureService,
//                docs/ImageTextify/ImageTextify-Phase10-Live-Transcription-Plan.md
// NOTES
//   - WAV only in Phase 10.F. MP3 is a future enum value, additive.
//   - The output is 16 kHz mono PCM16 WAV, matching the capture format
//     (NAudioAudioCaptureService WithFormat fix).
//   - WriteAudio is called by TeeStream as bytes arrive from the capture
//     stream. It must be thread-safe against concurrent writes from the
//     capture thread and reads from StopAsync.
//   - Recording is opt-in: the ViewModel calls StartAsync only when the
//     user enables the audio recording toggle. Transcription alone never
//     creates a recording.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// Opt-in live audio recording to a file on disk.
/// </summary>
public interface ILiveRecordingService : IAsyncDisposable
{
    /// <summary>True while the service is recording to a file.</summary>
    bool IsRecording { get; }

    /// <summary>
    /// Starts recording to <see cref="RecordingOptions.OutputPath"/>.
    /// </summary>
    /// <param name="options">The recording options. Not null.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="options"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when already recording.
    /// </exception>
    Task StartAsync(RecordingOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Stops recording and finalizes the output file.
    /// </summary>
    /// <param name="cancellationToken">Cancels the stop.</param>
    /// <returns>The recorded file's path, duration, and byte count.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when not recording.
    /// </exception>
    Task<RecordingResult> StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns a stream that, when recording is active, mirrors every read
    /// from <paramref name="captureStream"/> to the recording sink. When
    /// recording is not active, returns <paramref name="captureStream"/>
    /// unchanged.
    /// </summary>
    /// <param name="captureStream">The audio capture stream. Not null.</param>
    /// <returns>The stream the segmenter should read from.</returns>
    Stream WrapCaptureStream(Stream captureStream);

}

/// <summary>
/// Options controlling a live recording.
/// </summary>
/// <param name="OutputPath">The absolute WAV output path. Not null.</param>
/// <param name="Format">The container format. Only WAV is supported in Phase 10.F.</param>
/// <param name="SampleRateHz">Sample rate. Defaults to 16000.</param>
/// <param name="Channels">Channel count. Defaults to 1.</param>
public sealed record RecordingOptions(
    string OutputPath,
    RecordingFormat Format = RecordingFormat.Wav,
    int SampleRateHz = 16000,
    int Channels = 1);

/// <summary>
/// The recording container format.
/// </summary>
public enum RecordingFormat
{
    /// <summary>
    /// Uncompressed 16 kHz mono PCM16 WAV. The only format in Phase 10.F.
    /// </summary>
    Wav = 0,
}

/// <summary>
/// The result of a completed recording.
/// </summary>
/// <param name="OutputPath">The written file's absolute path. Not null.</param>
/// <param name="Duration">The recorded audio duration.</param>
/// <param name="BytesWritten">The file size in bytes.</param>
public sealed record RecordingResult(
    string OutputPath,
    TimeSpan Duration,
    long BytesWritten);