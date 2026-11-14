// ============================================================================
// FILE:     src/ImageTextify.Core/Domain/ImageTextifyCategory.cs
// PURPOSE:  The user-selected intake category. Determines which extension
//           allowlist applies during validation and which extraction
//           pipeline processes the file in a later phase.
// RELATION: Consumed by the validator contract and the intake queue.
// ============================================================================

namespace ImageTextify.Core.Domain;

/// <summary>
/// The user-selected intake category.
/// </summary>
public enum ImageTextifyCategory
{
    /// <summary>
    /// Still images: printed text, handwriting, photographed documents.
    /// Allowed extensions: .png, .jpg, .jpeg, .tif, .tiff, .bmp, .webp.
    /// </summary>
    Image = 0,

    /// <summary>
    /// PDF documents. Allowed extension: .pdf.
    /// </summary>
    Pdf = 1,

    /// <summary>
    /// Audio files. Allowed extensions: .wav, .mp3, .m4a, .flac, .ogg.
    /// </summary>
    Audio = 2,

    /// <summary>
    /// Video files. Allowed extensions: .mp4, .mkv, .mov, .avi, .webm.
    /// </summary>
    Video = 3,
}