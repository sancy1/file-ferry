// filepath: src/ImageTextify.Platform/Audio/LiveRecordingService.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Audio
// since:    Phase 10 Block 10.F.3c-b
// purpose:  Opt-in live audio recording to a 16 kHz mono PCM16 WAV. The
//           service opens the output file, writes the WAV header, and
//           exposes a TransformingSinkStream that TeeStream can mirror
//           captured float32 audio into. Recording is a separate explicit
//           user action; transcription never implies recording.
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.ILiveRecordingService
//   Depends on : TransformingSinkStream, TeeStream,
//                RecordingOptions, RecordingFormat, RecordingResult
//   Used by    : ImageTextify.Platform.Audio.LiveTranscriptionService
//                (constructs one per session when the recording toggle is on)
//   See also   : ImageTextify.Core.Abstractions.ILiveRecordingService,
//                ImageTextify.Platform.Audio.TransformingSinkStream,
//                ImageTextify.Platform.Audio.TeeStream
// NOTES
//   - WAV only in Phase 10.F. No MP3, no encoder, no external process.
//   - The output is 16 kHz mono PCM16 WAV by default; the caller may pass
//     a different RecordingOptions.SampleRateHz or Channels but the capture
//     adapter emits 16 kHz mono so the defaults are the correct values.
//   - The WAV header is written twice: once at StartAsync with dataSize=0,
//     and again at StopAsync with the final sizes. The file is seekable.
//   - Ownership: the service owns the FileStream and the sink. DisposeAsync
//     finalizes the header (via StopAsync) if still recording, then
//     disposes the sink (which disposes the FileStream).
//   - StopAsync does not check the cancellation token. A cancelled stop
//     that leaves the file without a final header is worse than a fast
//     non-cancellable stop. The token parameter exists for the interface.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;

namespace ImageTextify.Platform.Audio;

/// <summary>
/// Opt-in live audio recording to a 16 kHz mono PCM16 WAV file.
/// </summary>
public sealed class LiveRecordingService : ILiveRecordingService
{
    private readonly object _lock = new();
    private FileStream? _fileStream;
    private TransformingSinkStream? _sink;
    private string? _outputPath;
    private int _sampleRateHz;
    private int _channels;
    private bool _isRecording;

    /// <inheritdoc/>
    public bool IsRecording => _isRecording;

    /// <inheritdoc/>
    public Task StartAsync(RecordingOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (_isRecording)
            {
                throw new InvalidOperationException("Already recording.");
            }

            var dir = Path.GetDirectoryName(options.OutputPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var fs = new FileStream(
                options.OutputPath,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.Read);
            try
            {
                WriteWavHeader(fs, options.SampleRateHz, options.Channels, dataSize: 0);
                fs.Flush(true);

                _fileStream = fs;
                _sink = new TransformingSinkStream(fs);
                _outputPath = options.OutputPath;
                _sampleRateHz = options.SampleRateHz;
                _channels = options.Channels;
                _isRecording = true;
            }
            catch
            {
                try { fs.Dispose(); } catch { }
                throw;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<RecordingResult> StopAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (!_isRecording || _fileStream is null)
            {
                throw new InvalidOperationException("Not recording.");
            }

            _isRecording = false;

            try { _sink?.Flush(); } catch { }

            long dataBytes = _fileStream.Length - 44;
            if (dataBytes < 0) { dataBytes = 0; }

            _fileStream.Position = 0;
            WriteWavHeader(_fileStream, _sampleRateHz, _channels, (int)dataBytes);
            _fileStream.Flush(true);

            try { _sink?.Dispose(); } catch { }
            _sink = null;
            _fileStream = null;

            double seconds = dataBytes / (_sampleRateHz * (double)_channels * 2.0);
            var duration = TimeSpan.FromSeconds(seconds);
            var result = new RecordingResult(_outputPath ?? string.Empty, duration, dataBytes + 44);
            return Task.FromResult(result);
        }
    }

    /// <inheritdoc/>
    public Stream WrapCaptureStream(Stream captureStream)
    {
        ArgumentNullException.ThrowIfNull(captureStream);
        if (!_isRecording || _sink is null)
        {
            return captureStream;
        }
        return new TeeStream(captureStream, _sink);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_isRecording)
        {
            try { await StopAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
        }

        lock (_lock)
        {
            try { _sink?.Dispose(); } catch { }
            _sink = null;
            try { _fileStream?.Dispose(); } catch { }
            _fileStream = null;
            _isRecording = false;
        }
    }

    private static void WriteWavHeader(Stream stream, int sampleRateHz, int channels, int dataSize)
    {
        using var bw = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataSize);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)channels);
        bw.Write(sampleRateHz);
        bw.Write(sampleRateHz * channels * 2);
        bw.Write((short)(channels * 2));
        bw.Write((short)16);
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);
        bw.Flush();
    }
}