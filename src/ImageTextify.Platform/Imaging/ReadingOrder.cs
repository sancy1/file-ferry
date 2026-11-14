// ============================================================================
// FILE:     src/ImageTextify.Platform/Imaging/ReadingOrder.cs
// PURPOSE:  Sorts detected text regions into reading order. Pure geometry.
//           No OCR dependency. Column detection via X-clustering, then
//           top-to-bottom within each column, with a top-to-bottom fallback
//           when no column structure is detected.
// RELATION: Consumed by the OCR engine in Phase 2.4b.
// ============================================================================

namespace ImageTextify.Platform.Imaging;

/// <summary>
/// Sorts text regions into reading order.
/// </summary>
/// <remarks>
/// <para>
/// The algorithm detects columns by looking for horizontal gaps larger than
/// a threshold derived from the median region width. Regions are then sorted
/// column by column, and top-to-bottom within each column.
/// </para>
/// <para>
/// When no column structure is detected, the algorithm falls back to a
/// top-to-bottom sort with a small row tolerance so that regions on the same
/// visual line stay together.
/// </para>
/// <para>
/// The method is pure and deterministic. It never mutates its input.
/// </para>
/// </remarks>
public static class ReadingOrder
{
    /// <summary>
    /// Sorts the regions into reading order and returns their indexes.
    /// </summary>
    /// <param name="regions">
    /// The detected text regions. Not null. May be empty.
    /// </param>
    /// <param name="options">
    /// Options controlling the sort. Not null.
    /// </param>
    /// <returns>
    /// The indexes in reading order, referencing positions in
    /// <paramref name="regions"/>. Never null; empty when the input is empty.
    /// </returns>
    public static IReadOnlyList<int> Sort(
        IReadOnlyList<TextRegion> regions,
        ReadingOrderOptions options)
    {
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(options);

        if (regions.Count == 0)
        {
            return Array.Empty<int>();
        }

        if (regions.Count == 1)
        {
            return new[] { 0 };
        }

        var columns = DetectColumns(regions, options);
        if (columns.Count <= 1)
        {
            return SortByRows(regions, options);
        }

        var result = new List<int>(regions.Count);
        foreach (var column in columns)
        {
            var sorted = column
                .OrderBy(r => r.Y)
                .ThenBy(r => r.X)
                .ThenBy(r => r.Index)
                .Select(r => r.Index);
            result.AddRange(sorted);
        }

        return result;
    }

    private static List<List<TextRegion>> DetectColumns(
        IReadOnlyList<TextRegion> regions,
        ReadingOrderOptions options)
    {
        var sortedByX = regions.OrderBy(r => r.X).ToList();
        var medianWidth = Median(sortedByX.Select(r => r.Width).ToList());
        var gapThreshold = options.ColumnGapThresholdPx ?? Math.Max(1, medianWidth);

        var columns = new List<List<TextRegion>>();
        var current = new List<TextRegion> { sortedByX[0] };
        var currentRight = sortedByX[0].X + sortedByX[0].Width;

        for (var i = 1; i < sortedByX.Count; i++)
        {
            var r = sortedByX[i];
            if (r.X - currentRight >= gapThreshold)
            {
                columns.Add(current);
                current = new List<TextRegion> { r };
                currentRight = r.X + r.Width;
            }
            else
            {
                current.Add(r);
                currentRight = Math.Max(currentRight, r.X + r.Width);
            }
        }

        columns.Add(current);

        return columns.Count > 1
            ? columns
            : new List<List<TextRegion>> { sortedByX };
    }

    private static List<int> SortByRows(
        IReadOnlyList<TextRegion> regions,
        ReadingOrderOptions options)
    {
        var sortedByY = regions.OrderBy(r => r.Y).ToList();
        var rows = new List<List<TextRegion>>();

        foreach (var r in sortedByY)
        {
            var matched = false;
            foreach (var row in rows)
            {
                var rowY = row[0].Y;
                if (Math.Abs(r.Y - rowY) <= options.RowTolerancePx)
                {
                    row.Add(r);
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                rows.Add(new List<TextRegion> { r });
            }
        }

        var result = new List<int>(regions.Count);
        foreach (var row in rows)
        {
            var ordered = row
                .OrderBy(r => r.X)
                .ThenBy(r => r.Index)
                .Select(r => r.Index);
            result.AddRange(ordered);
        }

        return result;
    }

    private static int Median(List<int> values)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        return values[values.Count / 2];
    }
}
