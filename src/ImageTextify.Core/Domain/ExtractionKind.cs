// ============================================================================
// FILE:     src/ImageTextify.Core/Domain/ExtractionKind.cs
// PURPOSE:  The category of an ImageTextify source file. Fixed by the
//           wire contract of the queue and the export schema; a new kind
//           is a code change, not a configuration.
// RELATION: Consumed by ImageTextifySourceFile, by the queue view model,
//           by per-file validation, by the result separator, and by the
//           export metadata.
// ============================================================================

namespace ImageTextify.Core.Domain;

/// <summary>
/// The category of an ImageTextify source file.
/// </summary>
public enum ExtractionKind
{
    /// <summary>
    /// A still image. Printed text, handwriting, and photographed
    /// documents all fall into this category. The OCR engine
    /// distinguishes them; the queue does not.
    /// </summary>
    Image = 0,

    /// <summary>
    /// A PDF document. Pages with an embedded text layer are read
    /// directly; scanned pages are rendered to images and OCR runs.
    /// </summary>
    Pdf = 1,

    /// <summary>
    /// An audio file. The audio is prepared to 16 kHz mono PCM and
    /// transcribed by the local speech model.
    /// </summary>
    Audio = 2,

    /// <summary>
    /// A video file. The audio track is extracted and transcribed;
    /// the video itself is never converted unless the user explicitly
    /// requests a converted copy.
    /// </summary>
    Video = 3,
}