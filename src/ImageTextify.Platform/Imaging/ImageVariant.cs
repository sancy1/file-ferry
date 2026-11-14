// ============================================================================
// FILE:     src/ImageTextify.Platform/Imaging/ImageVariant.cs
// PURPOSE:  Identifies which preprocessing variant a bitmap represents.
//           The OCR engine may run on either variant; the result records
//           which one produced each line.
// RELATION: Consumed by ImagePreprocessingResult and by the OCR engine.
// ============================================================================

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// Identifies a preprocessing variant.
/// </summary>
public enum ImageVariant
{
    /// <summary>The decoded image, unmodified.</summary>
    Original = 0,

    /// <summary>The image after the deterministic preprocessing pipeline.</summary>
    Enhanced = 1,
}