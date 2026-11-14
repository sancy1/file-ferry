// ============================================================================
// FILE:     src/ImageTextify.Platform/Imaging/TextRegion.cs
// PURPOSE:  A single detected text region, used by the reading-order sort.
//           Geometry only. No text content.
// RELATION: Consumed by ReadingOrder.Sort.
// ============================================================================

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// A text region with its bounding box.
/// </summary>
/// <param name="Index">
/// The caller's stable index for this region. Preserved through the sort.
/// </param>
/// <param name="X">The left edge in pixels.</param>
/// <param name="Y">The top edge in pixels.</param>
/// <param name="Width">The width in pixels. Positive.</param>
/// <param name="Height">The height in pixels. Positive.</param>
/// <param name="Confidence">
/// The detector's confidence in this region, in the range zero to one.
/// </param>
public sealed record TextRegion(
    int Index,
    int X,
    int Y,
    int Width,
    int Height,
    double Confidence);