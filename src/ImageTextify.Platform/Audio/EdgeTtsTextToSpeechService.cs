// ============================================================================
// filepath: src/ImageTextify.Platform/Audio/EdgeTtsTextToSpeechService.cs
// layer:    Platform / Infrastructure
// module:   ImageTextify.Platform.Audio
// since:    Phase 13 (Text-to-Speech)
// purpose:  Implements ITextToSpeechService over Microsoft Edge neural TTS
//           (EdgeTTS.DotNet, no API key). Natural voices; the text is sent to
//           Microsoft's service (accepted cloud tradeoff). Edge emits MP3, so
//           live playback goes through NAudio and file synthesis is decoded
//           to WAV with NAudio before the atomic move.
//
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.ITextToSpeechService
//   Depends on : EdgeTTS.DotNet (Communicate), NAudio (WaveOut,
//                Mp3FileReader, WaveFileWriter)
//   Used by    : FallbackTextToSpeechService (primary engine);
//                ImageTextifyServiceCollectionExtensions (DI)
//   See also   : SystemSpeechTextToSpeechService.cs (the offline fallback),
//                ITextToSpeechService.cs (the contract)
//
// NOTES
//   - Voice: en-US-EmmaMultilingualNeural (top-rated natural voice).
//   - Rate "-5%": slight slowdown; all neural voices sound more human.
//   - SpeakAsync synthesizes the full utterance to a temp MP3 first, then
//     plays it through WaveOut. Latency is one round-trip; Edge TTS is
//     fast enough that this is imperceptible for typical text lengths.
//   - Pause/Resume/Stop map to WaveOut.Pause/Play/Stop. IsSpeaking
//     reports Playing or Paused.
//   - SynthesizeToFileAsync writes WAV (the contract requires WAV): the MP3
//     is decoded with Mp3FileReader and re-encoded with WaveFileWriter.
//   - Every temp file is deleted in finally; the target is written via
//     temp-then-move so no partial file is left behind.
// ============================================================================

using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using EdgeTTS.DotNet;
using ImageTextify.Core.Abstractions;
using NAudio.Wave;

namespace ImageTextify.Platform.Audio;

/// <summary>
/// ITextToSpeechService over Microsoft Edge neural TTS (cloud, no key).
/// Windows-only in practice: NAudio playback is Windows-only.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class EdgeTtsTextToSpeechService : ITextToSpeechService, IDisposable
{
    private const string Voice = "en-US-EmmaMultilingualNeural";
    private const string Rate = "-5%";

    private readonly object _gate = new();
    private WaveOut? _player;
    private Mp3FileReader? _reader;
    private bool _disposed;

    /// <inheritdoc/>
    public bool IsSpeaking
    {
        get
        {
            lock (_gate)
            {
                if (_disposed || _player is null) return false;
                return _player.PlaybackState is PlaybackState.Playing or PlaybackState.Paused;
            }
        }
    }

    /// <inheritdoc/>
    public async Task SpeakAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) return;
        cancellationToken.ThrowIfCancellationRequested();

        var mp3Path = Path.Combine(Path.GetTempPath(), "edge-tts-" + Guid.NewGuid().ToString("N") + ".mp3");
        try
        {
            var request = new Communicate(text, voice: Voice, rate: Rate);
            await request.SaveAsync(mp3Path, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            WaveOut? player;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                StopPlaybackLocked();
                _reader = new Mp3FileReader(mp3Path);
                player = new WaveOut();
                _player = player;
                player.PlaybackStopped += (_, _) => tcs.TrySetResult(null);
                player.Init(_reader);
                player.Play();
            }

            using var reg = cancellationToken.Register(() =>
            {
                try { player.Stop(); } catch { }
            });

            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (_gate) { StopPlaybackLocked(); }
            try { if (File.Exists(mp3Path)) File.Delete(mp3Path); } catch { }
        }
    }

    /// <inheritdoc/>
    public void Pause()
    {
        lock (_gate)
        {
            if (_disposed || _player is null) return;
            try { _player.Pause(); } catch { }
        }
    }

    /// <inheritdoc/>
    public void ResumeSpeaking()
    {
        lock (_gate)
        {
            if (_disposed || _player is null) return;
            try { _player.Play(); } catch { }
        }
    }

    /// <inheritdoc/>
    public void StopSpeaking()
    {
        lock (_gate)
        {
            if (_disposed) return;
            StopPlaybackLocked();
        }
    }

    /// <inheritdoc/>
    public async Task<string> SynthesizeToFileAsync(string text, string outputPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (text.Length == 0)
            throw new ArgumentException("Text is empty; nothing to synthesize.", nameof(text));
        cancellationToken.ThrowIfCancellationRequested();

        var mp3Path = Path.Combine(Path.GetTempPath(), "edge-tts-" + Guid.NewGuid().ToString("N") + ".mp3");
        var wavTemp = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var request = new Communicate(text, voice: Voice, rate: Rate);
            await request.SaveAsync(mp3Path, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Run(() =>
            {
                using var reader = new Mp3FileReader(mp3Path);
                using var writer = new WaveFileWriter(wavTemp, reader.WaveFormat);
                reader.CopyTo(writer);
            }, cancellationToken).ConfigureAwait(false);

            if (!File.Exists(wavTemp) || new FileInfo(wavTemp).Length == 0)
                throw new InvalidOperationException("Synthesis produced no WAV file.");
            File.Move(wavTemp, outputPath, overwrite: true);
            return outputPath;
        }
        finally
        {
            try { if (File.Exists(mp3Path)) File.Delete(mp3Path); } catch { }
            try { if (File.Exists(wavTemp)) File.Delete(wavTemp); } catch { }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            StopPlaybackLocked();
        }
    }

    // Caller must hold _gate.
    private void StopPlaybackLocked()
    {
        try { _player?.Stop(); } catch { }
        try { _player?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        _player = null;
        _reader = null;
    }
}
