// ============================================================================
// FILE:     src/ImageTextify.Platform/Imaging/ReadingOrderOptions.cs
// PURPOSE:  Options for the reading-order sort. The defaults are tuned for
//           typical documents: two columns, modest row tolerance.
// RELATION: Consumed by ReadingOrder.Sort.
// ============================================================================

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// Options for the reading-order sort.
/// </summary>
/// <param name="ColumnGapThresholdPx">
/// The horizontal gap, in pixels, above which a gap between regions is
/// treated as a column boundary. Default is the median region width.
/// A null value uses the median-derived default.
/// </param>
/// <param name="RowTolerancePx">
/// The vertical tolerance, in pixels, used to decide two regions are on
/// the same row. Default 8.
/// </param>
public sealed record ReadingOrderOptions(
    int? ColumnGapThresholdPx = null,
    int RowTolerancePx = 8);