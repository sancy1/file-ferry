// ============================================================================
// filepath: src/ImageTextify.Core/Abstractions/ITextToSpeechService.cs
// layer:    Contracts
// module:   ImageTextify.Core.Abstractions
// since:    Phase 13 (Text-to-Speech)
// purpose:  Contract for reading text aloud (live to speakers) and producing a
//           WAV file from the same text. Library-agnostic: the implementation
//           wraps a concrete synthesizer; this interface names only BCL types.
//
// RELATIONSHIPS
//   Implements : n/a (interface)
//   Depends on : n/a (BCL only)
//   Used by    : ImageTextify.Platform.Audio.SystemSpeechTextToSpeechService
//                (the platform wrapper),
//                FileHarvester.App.ViewModels.TextToSpeechViewModel,
//                FileHarvester.App.Views.TextToSpeechPage.axaml
//   See also   : Phase 13 design plan;
//                SystemSpeechTextToSpeechService.cs (the implementation)
//
// NOTES
//   - Live playback (SpeakAsync) and file synthesis (SynthesizeToFileAsync) are
//     independent. SpeakAsync plays to the default audio device; it does not
//     write a file. SynthesizeToFileAsync writes a file; it does not play to
//     speakers. The UI exposes both as separate actions.
//   - Pause and Resume apply only to live playback (the plan says so; the
//     platform implementation cannot pause a file write once it has started).
//     Calling Pause when nothing is speaking is a no-op, not an error.
//   - The implementation is Windows-only because System.Speech is a
//     Windows-provided package on .NET 10 (finding F-008-adjacent; the
//     registration in ImageTextifyServiceCollectionExtensions is guarded).
//   - Empty or whitespace-only text is rejected by the platform implementation
//     with an ArgumentException, not by this interface. The contract does not
//     constrain text beyond "not null."
//   - SynthesizeToFileAsync returns the produced file's absolute path so the
//     UI can show exactly what was written (parity with IConversionService;
//     finding F-041 records the deviation from the phase 13 plan's void form).
// ============================================================================

using System.Threading;
using System.Threading.Tasks;

namespace ImageTextify.Core.Abstractions;

/// <summary>
/// Reads text aloud (live) and produces a WAV file from the same text.
/// </summary>
public interface ITextToSpeechService
{
    /// <summary>
    /// Speaks <paramref name="text"/> aloud on the default audio device.
    /// </summary>
    /// <param name="text">The text to speak. Not null; empty is a no-op.</param>
    /// <param name="cancellationToken">
    /// Cancels the speech. On cancellation, the utterance stops as soon as the
    /// engine reaches a boundary; any already-spoken audio has already played.
    /// </param>
    /// <returns>
    /// A task that completes when the utterance ends (spoken or cancelled).
    /// </returns>
    Task SpeakAsync(string text, CancellationToken cancellationToken);

    /// <summary>
    /// Pauses live playback. No-op when nothing is speaking.
    /// </summary>
    void Pause();

    /// <summary>
    /// Resumes live playback. No-op when nothing is paused.
    /// </summary>
    void ResumeSpeaking();

    /// <summary>
    /// Stops live playback immediately. No-op when nothing is speaking.
    /// </summary>
    void StopSpeaking();

    /// <summary>True while a live utterance is playing or paused.</summary>
    bool IsSpeaking { get; }

    /// <summary>
    /// Synthesizes <paramref name="text"/> into a WAV file at
    /// <paramref name="outputPath"/>. Does not play to speakers.
    /// </summary>
    /// <param name="text">The text to synthesize. Not null; empty is rejected.</param>
    /// <param name="outputPath">
    /// The absolute destination path. Not null or empty. The implementation
    /// creates or overwrites this file on success and creates no file on
    /// failure.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the synthesis. On cancellation, no output file is left behind.
    /// </param>
    /// <returns>
    /// A task that completes with the produced file's absolute path on success.
    /// The returned path equals <paramref name="outputPath"/> when successful;
    /// the value is returned for symmetry with IConversionService.
    /// </returns>
    Task<string> SynthesizeToFileAsync(string text, string outputPath, CancellationToken cancellationToken);
}
