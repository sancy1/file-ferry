// filepath: src/ImageTextify.Platform/Audio/NAudioAudioCaptureService.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Audio
// since:    Phase 10 Block 10.A
// purpose:  Implements IAudioCaptureService on Windows via NAudio WASAPI.
//           Supports microphone capture and system audio loopback; the
//           session exposes a readable Stream the live orchestrator pulls
//           from. Internally the session pumps NAudio's WasapiRecorder
//           CaptureAsync IAsyncEnumerable into a byte queue that backs the
//           exposed Stream.
//
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.IAudioCaptureService
//   Depends on : NAudio.Wasapi (WasapiRecorder, WasapiRecorderBuilder,
//                MMDeviceEnumerator)
//   Used by    : ImageTextify.Platform.Audio.LiveTranscriptionService (Block 10.C)
//   See also   : docs/ImageTextify/ImageTextify-Phase10-Live-Transcription-Plan.md,
//                docs/ImageTextify/ADR-002-streaming-whisper-strategy.md
//
// NOTES
//   - Windows-only. macOS and Linux adapters are Phase 10.E.
//   - NAudio 3.3.0 removed WasapiCapture and marked WasapiLoopbackCapture
//     obsolete. The supported path is WasapiRecorderBuilder -> Build() ->
//     WasapiRecorder, which exposes CaptureAsync as an IAsyncEnumerable.
//     The API surface used here is measured from the actual assembly via
//     NAudioApiSurfaceDiagnostic (F-AV-092).
//   - Loopback vs microphone is modeled by a DeviceId prefix: DeviceId
//     starting with "loopback:" selects system audio loopback; any other
//     DeviceId selects a microphone. This preserves the interface shape
//     as declared in Phase 1.3 (AudioCaptureOptions has no Mode field).
//   - Emits 16 kHz mono IEEE float32 directly. This matches
//     AudioCaptureOptions.SampleRateHz=16000 and Channels=1, and the
//     float32 sample shape WhisperVadProcessor expects. Downstream
//     consumers (Phase 10.B segmenter, Phase 10.C orchestrator) do not
//     resample or downmix; they receive canonical PCM.
//   - Recording is a separate opt-in service (Block 10.F). The session
//     does not retain audio.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ImageTextify.Platform.Audio;

/// <summary>
/// Windows microphone and loopback capture via NAudio WasapiRecorder.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NAudioAudioCaptureService : IAudioCaptureService
{
    private const string LoopbackPrefix = "loopback:";

    /// <inheritdoc/>
    public async Task<IAudioCaptureSession> StartAsync(
        AudioCaptureOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        ParseMode(options.DeviceId, out var isLoopback, out var deviceIdToResolve);

        try
        {
            var builder = new WasapiRecorderBuilder();
            if (isLoopback)
            {
                builder = builder.WithLoopbackCapture();
            }

            if (string.IsNullOrEmpty(deviceIdToResolve))
            {
                using var defaultEnum = new MMDeviceEnumerator();
                var defaultDev = defaultEnum.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                builder = builder.WithDevice(defaultDev);
            }
            else
            {
                var device = ResolveDevice(deviceIdToResolve, isLoopback);
                builder = builder.WithDevice(device);
            }

            builder = builder.WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(16000, 1));
            var recorder = await builder.BuildAsync().ConfigureAwait(false);
            var session = new NAudioCaptureSession(recorder);
            return session;
        }
        catch (AudioUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AudioUnavailableException(
                "Failed to open the audio capture device. Check Windows microphone permission and device availability.",
                ex);
        }
    }

    private static void ParseMode(string? rawDeviceId, out bool isLoopback, out string? deviceId)
    {
        isLoopback = false;
        deviceId = rawDeviceId;
        if (string.IsNullOrEmpty(rawDeviceId)) { return; }
        if (rawDeviceId.StartsWith(LoopbackPrefix, StringComparison.OrdinalIgnoreCase))
        {
            isLoopback = true;
            var suffix = rawDeviceId.Substring(LoopbackPrefix.Length);
            deviceId = string.IsNullOrEmpty(suffix) ? null : suffix;
        }
    }

    private static MMDevice ResolveDevice(string deviceId, bool isLoopback)
    {
        using var enumerator = new MMDeviceEnumerator();
        var dataFlow = isLoopback ? DataFlow.Render : DataFlow.Capture;
        var devices = enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active);
        foreach (var d in devices)
        {
            if (string.Equals(d.ID, deviceId, StringComparison.Ordinal)) { return d; }
        }
        throw new AudioUnavailableException("Audio device not found: " + deviceId);
    }
}

/// <summary>
/// One active NAudio capture session. Pumps the recorder's CaptureAsync
/// IAsyncEnumerable into a byte queue that backs the exposed Stream.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class NAudioCaptureSession : IAudioCaptureSession
{
    private readonly WasapiRecorder _recorder;
    private readonly ConcurrentQueue<byte[]> _chunks = new();
    private readonly CancellationChunkStream _stream;
    private readonly CancellationTokenSource _pumpCts = new();
    private readonly Task _pumpTask;
    private volatile bool _isActive;
    private int _stopRequested;
    private bool _diagnosed;

    public NAudioCaptureSession(WasapiRecorder recorder)
    {
        _recorder = recorder;
        try { System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "itx-capture-format.txt"), "Recorder WaveFormat: " + recorder.WaveFormat + Environment.NewLine); } catch { }
        _stream = new CancellationChunkStream(_chunks, _pumpCts.Token);
        _isActive = true;
        // BuildAsync returns a recorder that is already recording; do not call StartRecording.
        _pumpTask = PumpAsync(_pumpCts.Token);
    }

    public bool IsActive => _isActive;

    public Stream AudioStream => _stream;

    public WaveFormat Format => _recorder.WaveFormat;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _stopRequested, 1, 0) != 0)
        {
            return;
        }
        _isActive = false;
        try { _recorder.StopRecording(); } catch { }
        try { _pumpCts.Cancel(); } catch { }
        try { await _pumpTask.ConfigureAwait(false); } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        _isActive = false;
        try { _recorder.StopRecording(); } catch { }
        try { _pumpCts.Cancel(); } catch { }
        try { await _pumpTask.ConfigureAwait(false); } catch { }
        try { await _recorder.DisposeAsync().ConfigureAwait(false); } catch { }
        try { _pumpCts.Dispose(); } catch { }
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var buffer in _recorder.CaptureAsync(cancellationToken).ConfigureAwait(false))
            {
                // AudioBuffer exposes Data as ReadOnlyMemory<byte> at NAudio 3.3.0.
                // Measured by NAudioApiSurfaceDiagnostic; see F-AV-093.
                var mem = buffer.Data;
                if (mem.Length == 0) { continue; }
                if (!_diagnosed)
                {
                    _diagnosed = true;
                    try
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.Append("Recorder WaveFormat: ").Append(_recorder.WaveFormat).AppendLine();
                        sb.Append("First buffer bytes: ").Append(mem.Length).AppendLine();
                        var floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(mem.Span);
                        int n = Math.Min(10, floats.Length);
                        sb.Append("First ").Append(n).Append(" floats: ");
                        for (int k = 0; k < n; k++) { sb.Append(floats[k]).Append(' '); }
                        sb.AppendLine();
                        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "itx-capture-format.txt"), sb.ToString());
                    }
                    catch { }
                }
                var copy = mem.ToArray();
                _chunks.Enqueue(copy);
                _stream.SignalChunkAvailable();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            _isActive = false;
        }
    }
}

/// <summary>
/// A readable Stream over a queue of byte chunks. Blocks on Read when
/// empty until a chunk arrives or cancellation fires.
/// </summary>
internal sealed class CancellationChunkStream : Stream
{
    private readonly ConcurrentQueue<byte[]> _chunks;
    private readonly CancellationToken _cancellation;
    private readonly SemaphoreSlim _chunkSignal = new(0);
    private byte[]? _current;
    private int _currentOffset;

    public CancellationChunkStream(ConcurrentQueue<byte[]> chunks, CancellationToken cancellation)
    {
        _chunks = chunks;
        _cancellation = cancellation;
    }

    public void SignalChunkAvailable()
    {
        try { _chunkSignal.Release(); } catch { }
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (count <= 0) { return 0; }
        if (_current is null || _currentOffset >= _current.Length)
        {
            if (!WaitForChunk()) { return 0; }
        }
        int available = _current!.Length - _currentOffset;
        int toCopy = Math.Min(count, available);
        Array.Copy(_current, _currentOffset, buffer, offset, toCopy);
        _currentOffset += toCopy;
        return toCopy;
    }

    private bool WaitForChunk()
    {
        while (true)
        {
            if (_cancellation.IsCancellationRequested) { return false; }
            try
            {
                if (_chunkSignal.Wait(100))
                {
                    if (_chunks.TryDequeue(out var chunk))
                    {
                        _current = chunk;
                        _currentOffset = 0;
                        return true;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _chunkSignal.Dispose(); } catch { }
        }
        base.Dispose(disposing);
    }
}