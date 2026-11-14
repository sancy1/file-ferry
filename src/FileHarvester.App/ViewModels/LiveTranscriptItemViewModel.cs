// filepath: src/FileHarvester.App/ViewModels/LiveTranscriptItemViewModel.cs
// layer:    n/a (App ViewModel item)
// module:   FileHarvester.App.ViewModels
// since:    Phase 10 Block 10.D.1
// purpose:  One row in the live transcription transcript. Immutable in
//           Phase 10.D because interim text is deferred (ITX-10.C-013);
//           when interim lands, this class gains mutable Text plus change
//           notification. Phase 10.D ships final-only rows.
// RELATIONSHIPS
//   Implements : n/a
//   Depends on : n/a
//   Used by    : ImageTextify.Platform.Audio.LiveTranscriptionSession via
//                FileHarvester.App.ViewModels.LiveTranscriptionViewModel
//   See also   : FileHarvester.App.ViewModels.ImageTextifyResultItemViewModel
// NOTES
//   - Never touches Avalonia. Never holds a stream or a session reference.
//   - IsInterim is always false in Phase 10.D. The property exists so the
//     view template can bind to it without a code change when interim
//     arrives. A false value renders the row in normal (final) style.
//   - WarningsText joins quality warnings from the suppressor, or empty.

namespace FileHarvester.App.ViewModels;

/// <summary>
/// One live-transcription transcript row. Immutable in Phase 10.D; interim
/// rows will be mutable in a later block.
/// </summary>
public sealed class LiveTranscriptItemViewModel
{
    /// <summary>
    /// Creates the transcript row.
    /// </summary>
    /// <param name="timestampText">
    /// The session-relative time of the utterance, formatted for display.
    /// Not null; empty is acceptable when the timestamp is not known.
    /// </param>
    /// <param name="text">
    /// The recognized text. Not null; may be empty for a suppressed-only
    /// utterance.
    /// </param>
    /// <param name="isInterim">
    /// True when this row is an interim update. Always false in Phase 10.D
    /// (interim text deferred, ITX-10.C-013).
    /// </param>
    /// <param name="warningsText">
    /// The quality warnings joined, or empty. Not null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="timestampText"/>, <paramref name="text"/>,
    /// or <paramref name="warningsText"/> is null.
    /// </exception>
    public LiveTranscriptItemViewModel(
        string timestampText,
        string text,
        bool isInterim,
        string warningsText)
    {
        TimestampText = timestampText ?? throw new ArgumentNullException(nameof(timestampText));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        IsInterim = isInterim;
        WarningsText = warningsText ?? throw new ArgumentNullException(nameof(warningsText));
    }

    /// <summary>The session-relative timestamp, formatted for display.</summary>
    public string TimestampText { get; }

    /// <summary>The recognized text. May be empty.</summary>
    public string Text { get; }

    /// <summary>True when the row is an interim update. False in Phase 10.D.</summary>
    public bool IsInterim { get; }

    /// <summary>The quality warnings joined, or empty.</summary>
    public string WarningsText { get; }
}