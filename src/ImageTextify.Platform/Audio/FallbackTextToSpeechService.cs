// ============================================================================
// filepath: src/ImageTextify.Platform/Audio/FallbackTextToSpeechService.cs
// layer:    Platform / Infrastructure
// module:   ImageTextify.Platform.Audio
// since:    Phase 13 (Text-to-Speech)
// purpose:  Decorator over two ITextToSpeechService engines: the primary
//           (Edge neural TTS, cloud) and the fallback (System.Speech SAPI,
//           local). Any failure from the primary — network down, Microsoft
//           blocking the unofficial endpoint, DNS, TLS — delegates the same
//           call to the fallback so the feature keeps working offline.
//
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.ITextToSpeechService
//   Depends on : ITextToSpeechService (two instances)
//   Used by    : ImageTextifyServiceCollectionExtensions (DI, as the
//                registered ITextToSpeechService)
//   See also   : EdgeTtsTextToSpeechService.cs, SystemSpeechTextToSpeechService.cs
//
// NOTES
//   - Only non-cancellation exceptions trigger the fallback; a user cancel
//     is never retried on the second engine.
//   - Pause/Resume/Stop/IsSpeaking forward to BOTH engines: whichever one
//     is actually speaking responds; the idle one no-ops.
//   - If the primary fails AFTER partially playing, the fallback replays
//     from the start. Acceptable: the alternative is silence.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;

namespace ImageTextify.Platform.Audio;

/// <summary>
/// ITextToSpeechService that tries the primary engine and falls back to the
/// secondary on any non-cancellation failure.
/// </summary>
internal sealed class FallbackTextToSpeechService : ITextToSpeechService, IDisposable
{
    private readonly ITextToSpeechService _primary;
    private readonly ITextToSpeechService _fallback;
    private bool _disposed;

    /// <summary>
    /// Creates the decorator.
    /// </summary>
    /// <param name="primary">The preferred engine (Edge neural TTS).</param>
    /// <param name="fallback">The offline engine (System.Speech).</param>
    public FallbackTextToSpeechService(ITextToSpeechService primary, ITextToSpeechService fallback)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    /// <inheritdoc/>
    public bool IsSpeaking => _primary.IsSpeaking || _fallback.IsSpeaking;

    /// <inheritdoc/>
    public async Task SpeakAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            await _primary.SpeakAsync(text, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _fallback.SpeakAsync(text, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public void Pause()
    {
        _primary.Pause();
        _fallback.Pause();
    }

    /// <inheritdoc/>
    public void ResumeSpeaking()
    {
        _primary.ResumeSpeaking();
        _fallback.ResumeSpeaking();
    }

    /// <inheritdoc/>
    public void StopSpeaking()
    {
        _primary.StopSpeaking();
        _fallback.StopSpeaking();
    }

    /// <inheritdoc/>
    public async Task<string> SynthesizeToFileAsync(string text, string outputPath, CancellationToken cancellationToken)
    {
        try
        {
            return await _primary.SynthesizeToFileAsync(text, outputPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await _fallback.SynthesizeToFileAsync(text, outputPath, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_primary is IDisposable d1) d1.Dispose();
        if (_fallback is IDisposable d2) d2.Dispose();
    }
}
