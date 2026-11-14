// ============================================================================
// filepath: src/ImageTextify.Platform/Audio/SystemSpeechTextToSpeechService.cs
// layer:    Platform / Infrastructure
// module:   ImageTextify.Platform.Audio
// since:    Phase 13 (Text-to-Speech)
// purpose:  Implements ITextToSpeechService over System.Speech.Synthesis.
//           Live playback via SpeakAsync; file synthesis via SetOutputToWaveFile.
//           WAV only; the SAPI desktop voices are robotic by design.
//
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.ITextToSpeechService
//   Depends on : System.Speech.Synthesis (SpeechSynthesizer, SynthesizerState,
//                Prompt, SpeakCompletedEventArgs)
//   Used by    : ImageTextifyServiceCollectionExtensions (DI, Windows-guarded);
//                FileHarvester.App.ViewModels.TextToSpeechViewModel
//   See also   : ITextToSpeechService.cs (the contract);
//                Phase 13 design plan (section 2, section 3)
//
// NOTES
//   - Windows-only: the class carries [SupportedOSPlatform("windows")] so the
//     analyzer treats every SpeechSynthesizer call site as guarded. The DI
//     registration is what actually gates availability (step 3).
//   - One SpeechSynthesizer per instance. Registered as a singleton: the
//     synthesizer owns the audio device and the current voice selection.
//   - SynthesizeToFileAsync is atomic: temp file then File.Move. No file is
//     left at the target on failure or cancellation.
//   - SpeakAsync uses SpeakAsync + SpeakCompleted to await completion. The
//     synchronous Speak would block the UI thread. Cancellation is via
//     SpeakAsyncCancelAll; SpeakCompleted fires with Cancelled = true and the
//     TaskCompletionSource is transitioned to Canceled.
//   - Pause applies only to live playback; no-op when nothing is speaking.
//     ResumeSpeaking and StopSpeaking are likewise idempotent.
//   - CA1716 forced Resume -> ResumeSpeaking and Stop -> StopSpeaking on the
//     interface (finding F-042). The class matches the interface names.
//   - This file had 16 build errors when first written (finding F-044):
//     SpeakAsyncCanceled event does not exist (3), CA1416 Windows-only call
//     sites (11), CA2016 Token forward (1), CA1513 ObjectDisposedException
//     (2). All fixed in this version.
// ============================================================================

using System;
using System.IO;
using System.Runtime.Versioning;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;

namespace ImageTextify.Platform.Audio;

/// <summary>
/// ITextToSpeechService over System.Speech.Synthesis.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SystemSpeechTextToSpeechService : ITextToSpeechService, IDisposable
{
    private readonly SpeechSynthesizer _synth;
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>
    /// Creates the service with its own <see cref="SpeechSynthesizer"/>.
    /// </summary>
    public SystemSpeechTextToSpeechService()
    {
        _synth = new SpeechSynthesizer();
        // SAPI's default Rate (0) sounds fast on some voices; -2 is the
        // documented "slower" step. Applies to live speech and WAV output.
        _synth.Rate = -2;
        // Warm-up: a silent utterance forces the audio pipeline open so the
        // first phoneme of a real SpeakAsync is not clipped.
        try { _synth.Speak(" "); } catch { }
    }

    /// <inheritdoc/>
    public bool IsSpeaking
    {
        get
        {
            lock (_gate)
            {
                if (_disposed) return false;
                return _synth.State == SynthesizerState.Speaking;
            }
        }
    }

    /// <inheritdoc/>
    public async Task SpeakAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) return;
        if (cancellationToken.IsCancellationRequested) return;

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // WHAT: subscribe to the completion event, fire the async speak, and
        //       transition the TCS when it ends (spoken or cancelled).
        // WHY:  SpeechSynthesizer has no SpeakAsyncCanceled event; cancellation
        //       is reported via SpeakCompleted with Cancelled = true.
        void OnCompleted(object? s, SpeakCompletedEventArgs e)
        {
            if (e.Cancelled) tcs.TrySetCanceled(CancellationToken.None);
            else tcs.TrySetResult(null);
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _synth.SpeakCompleted += OnCompleted;
            _synth.SpeakAsync(text);
        }

        using var reg = cancellationToken.Register(() =>
        {
            try { _synth.SpeakAsyncCancelAll(); } catch { }
        });

        try { await tcs.Task.ConfigureAwait(false); }
        finally
        {
            lock (_gate)
            {
                try { _synth.SpeakCompleted -= OnCompleted; } catch { }
            }
        }
    }

    /// <inheritdoc/>
    public void Pause()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try { _synth.Pause(); } catch { }
        }
    }

    /// <inheritdoc/>
    public void ResumeSpeaking()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try { _synth.Resume(); } catch { }
        }
    }

    /// <inheritdoc/>
    public void StopSpeaking()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try { _synth.SpeakAsyncCancelAll(); } catch { }
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

        var tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await Task.Run(() =>
            {
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    _synth.SetOutputToWaveFile(tempPath);
                    try { _synth.Speak(text); }
                    finally { _synth.SetOutputToNull(); }
                }
            }, cancellationToken).ConfigureAwait(false);

            if (!File.Exists(tempPath))
                throw new InvalidOperationException("Synthesis produced no WAV file.");
            File.Move(tempPath, outputPath, overwrite: true);
            return outputPath;
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            try { _synth.SpeakAsyncCancelAll(); } catch { }
            try { _synth.Dispose(); } catch { }
        }
    }
}