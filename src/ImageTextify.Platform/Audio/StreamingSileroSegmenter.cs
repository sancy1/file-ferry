// filepath: src/ImageTextify.Platform/Audio/StreamingSileroSegmenter.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Audio
// since:    Phase 10 Block 10.B.3
// purpose:  Implements IStreamingSpeechSegmenter using Whisper.net's Silero
//           VAD applied to a growing buffer in batch mode. Emits
//           UtteranceStarted when the batch VAD first reports a speech
//           region and UtteranceEnded when speech stops.
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.IStreamingSpeechSegmenter
//   Depends on : Whisper.net (WhisperVadFactory, WhisperVadProcessor,
//                VadSegmentData), IStreamingSpeechSegmenter (contract),
//                IVadSession, WhisperVadAdapter
//   Used by    : Phase 10.C LiveTranscriptionService
//   See also   : ImageTextify.Platform.Speech.WhisperNetSileroSegmenter,
//                ADR-002
// NOTES
//   - Batch VAD path. Every 1 second of accumulated audio is written to a
//     temp WAV and passed to DetectSpeechAsync(Stream). That is the same
//     API Phase 5 proved on real audio. The streaming NoReset path
//     produced zero segments on real microphone speech in Gate 10.3
//     testing; the batch path is the fix.
//   - The 512-sample Silero frame requirement does not apply here because
//     DetectSpeechAsync takes the whole WAV and does its own windowing.
//   - Single-use. Create one segmenter per live session.
//   - The 30-second utterance cap (ADR-002) force-ends an open utterance.

using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;
using Whisper.net;

namespace ImageTextify.Platform.Audio;

/// <summary>
/// Batch Silero VAD segmenter. Consumes a 16 kHz mono float32 byte stream,
/// runs the batch VAD once per second, and emits utterance boundaries.
/// </summary>
public sealed class StreamingSileroSegmenter : IStreamingSpeechSegmenter
{
    private readonly string _vadModelPath;
    private readonly bool _ownsVad;
    private IVadSession? _vad;

    private const int ChunkSamples = 1600;
    private const int ChunkBytes = ChunkSamples * 4;
    private const int EndSilenceChunks = 4;
    private const int MaxUtteranceSamples = 30 * 16000;
    private const string LogPath = "itx-segmenter-log.txt";
    private const int CheckEverySamplesDefault = 32000;
    private const int VadWindowSamplesDefault = 64000;

    private readonly int _checkEverySamples;
    private readonly int _vadWindowSamples;
    private readonly byte[] _readBuffer = new byte[ChunkBytes];
    private readonly System.Collections.Generic.List<float> _accumulated = new();
    private TimeSpan _utteranceStart;
    private TimeSpan _lastSeenEnd;
    private bool _utteranceOpen;
    private int _silentChunks;

    /// <summary>
    /// Constructs the segmenter with the Silero VAD model path.
    /// </summary>
    /// <param name="vadModelPath">Absolute path to the Silero VAD GGML model. Not null.</param>
    /// <exception cref="ArgumentNullException">Thrown when vadModelPath is null.</exception>
    public StreamingSileroSegmenter(string vadModelPath)
    {
        ArgumentNullException.ThrowIfNull(vadModelPath);
        _vadModelPath = vadModelPath;
        _ownsVad = true;
        _checkEverySamples = CheckEverySamplesDefault;
        _vadWindowSamples = VadWindowSamplesDefault;
    }

    /// <summary>
    /// Constructs the segmenter with a substituted VAD session. Tests only.
    /// </summary>
    /// <param name="vad">The VAD session. Not null. Caller owns disposal.</param>
    /// <param name="checkEverySamples">
    /// Samples between batch VAD checks. Defaults to the production cadence.
    /// </param>
    /// <param name="vadWindowSamples">
    /// Trailing window the batch VAD analyzes. Pass <see cref="int.MaxValue"/>
    /// to disable windowing (the whole accumulated buffer is analyzed and
    /// segment times are absolute from stream start).
    /// </param>
    internal StreamingSileroSegmenter(
        IVadSession vad,
        int checkEverySamples = CheckEverySamplesDefault,
        int vadWindowSamples = VadWindowSamplesDefault)
    {
        ArgumentNullException.ThrowIfNull(vad);
        _vad = vad;
        _vadModelPath = string.Empty;
        _ownsVad = false;
        _checkEverySamples = checkEverySamples;
        _vadWindowSamples = vadWindowSamples;
    }

    /// <inheritdoc/>
    public event EventHandler<UtteranceStartedEventArgs>? UtteranceStarted;

    /// <inheritdoc/>
    public event EventHandler<UtteranceEndedEventArgs>? UtteranceEnded;

    /// <inheritdoc/>
    public async Task RunAsync(Stream audioStream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audioStream);

        WhisperVadFactory? factory = null;
        if (_vad is null)
        {
            factory = WhisperVadFactory.FromPath(_vadModelPath);
            _vad = new WhisperVadAdapter(factory.CreateBuilder().WithThreshold(0.3f).Build());
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "itx-stream-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        int samplesSinceLastCheck = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int read = await ReadChunkAsync(audioStream, cancellationToken).ConfigureAwait(false);
                if (read == 0) { break; }

                int sampleCount = read / 4;
                var floats = MemoryMarshal.Cast<byte, float>(_readBuffer.AsSpan(0, sampleCount * 4)).ToArray();
                foreach (var f in floats) { _accumulated.Add(f); }
                samplesSinceLastCheck += floats.Length;

                if (samplesSinceLastCheck >= _checkEverySamples)
                {
                    samplesSinceLastCheck = 0;
                    try
                    {
                        double sumSq = 0; foreach (var s in _accumulated) { sumSq += s * s; }
                        double rms = _accumulated.Count > 0 ? Math.Sqrt(sumSq / _accumulated.Count) : 0;
                        var logLine = "CHECK samples=" + _accumulated.Count + " rms=" + rms.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
                        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), LogPath), logLine + Environment.NewLine);
                    }
                    catch { }

                    int windowStart = _accumulated.Count > _vadWindowSamples
                        ? _accumulated.Count - _vadWindowSamples
                        : 0;
                    int windowCount = _accumulated.Count - windowStart;
                    var window = new float[windowCount];
                    _accumulated.CopyTo(windowStart, window, 0, windowCount);
                    var wavPath = Path.Combine(tempDir, "buffer.wav");
                    WritePcm16Wav(wavPath, window);

                    IReadOnlyList<VadSegmentData> result;
                    try
                    {
                        using var wavStream = File.OpenRead(wavPath);
                        result = await _vad.DetectSpeechAsync(wavStream, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                    catch { result = Array.Empty<VadSegmentData>(); }

                    var offset = TimeSpan.FromSeconds(windowStart / 16000.0);
                    ApplyVadResult(OffsetSegments(result, offset), cancellationToken);
                }

                if (_utteranceOpen && _accumulated.Count >= MaxUtteranceSamples)
                {
                    ForceCloseUtterance();
                }
            }
        }
        finally
        {
            if (_utteranceOpen) { try { CloseUtterance(); } catch { } }
            if (_ownsVad) { _vad?.Dispose(); }
            _vad = null;
            factory?.Dispose();
            // temp dir retained for diagnostics: tempDir
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), LogPath), "RETAINED " + tempDir + Environment.NewLine); } catch { }
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (_ownsVad) { _vad?.Dispose(); }
        _vad = null;
        return ValueTask.CompletedTask;
    }

    private async ValueTask<int> ReadChunkAsync(Stream s, CancellationToken ct)
    {
        int offset = 0;
        while (offset < ChunkBytes)
        {
            if (ct.IsCancellationRequested) { return 0; }
            int n;
            try
            {
                n = await s.ReadAsync(_readBuffer.AsMemory(offset, ChunkBytes - offset), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return 0; }
            if (n == 0) { return offset; }
            offset += n;
        }
        return offset;
    }

    private void ApplyVadResult(IReadOnlyList<VadSegmentData> result, CancellationToken ct)
    {
        bool silent;
        TimeSpan thisMaxEnd = _lastSeenEnd;
        if (result.Count == 0)
        {
            silent = true;
        }
        else
        {
            var maxEnd = TimeSpan.MinValue;
            var minStart = TimeSpan.MaxValue;
            foreach (var seg in result)
            {
                if (seg.End > maxEnd) { maxEnd = seg.End; }
                if (seg.Start < minStart) { minStart = seg.Start; }
            }
            thisMaxEnd = maxEnd;
            silent = maxEnd <= _lastSeenEnd;
            if (!silent && !_utteranceOpen)
            {
                _utteranceStart = minStart;
                _utteranceOpen = true;
                _silentChunks = 0;
                UtteranceStarted?.Invoke(this, new UtteranceStartedEventArgs(_utteranceStart));
                try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), LogPath), "STARTED at " + _utteranceStart.TotalSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + Environment.NewLine); } catch { }
            }
        }

        if (silent)
        {
            if (_utteranceOpen)
            {
                _silentChunks++;
                if (_silentChunks >= EndSilenceChunks)
                {
                    CloseUtterance();
                }
            }
        }
        else
        {
            _silentChunks = 0;
            if (thisMaxEnd > _lastSeenEnd) { _lastSeenEnd = thisMaxEnd; }
        }

        ct.ThrowIfCancellationRequested();
    }

    private static IReadOnlyList<VadSegmentData> OffsetSegments(
        IReadOnlyList<VadSegmentData> segments, TimeSpan offset)
    {
        if (offset == TimeSpan.Zero || segments.Count == 0) { return segments; }
        var list = new VadSegmentData[segments.Count];
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            list[i] = new VadSegmentData(s.Start + offset, s.End + offset);
        }
        return list;
    }

    private static void WritePcm16Wav(string path, float[] samples)
    {
        int dataBytes = samples.Length * 2;
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataBytes);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)1);
        bw.Write(16000);
        bw.Write(32000);
        bw.Write((short)2);
        bw.Write((short)16);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dataBytes);
        for (int i = 0; i < samples.Length; i++)
        {
            double v = samples[i] * 32767.0;
            if (v > 32767.0) { v = 32767.0; }
            else if (v < -32768.0) { v = -32768.0; }
            bw.Write((short)v);
        }
    }

    private void CloseUtterance()
    {
        var samples = _accumulated.ToArray();
        var end = _lastSeenEnd > _utteranceStart ? _lastSeenEnd : _utteranceStart;
        _utteranceOpen = false;
        _silentChunks = 0;
        _accumulated.Clear();
        _vad?.ResetState();
        _lastSeenEnd = TimeSpan.Zero;
        UtteranceEnded?.Invoke(this, new UtteranceEndedEventArgs(samples, _utteranceStart, end));
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), LogPath), "ENDED start=" + _utteranceStart.TotalSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " end=" + end.TotalSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " samples=" + samples.Length + Environment.NewLine); } catch { }
    }

    private void ForceCloseUtterance()
    {
        CloseUtterance();
    }
}