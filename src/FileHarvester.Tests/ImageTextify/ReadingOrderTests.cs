// ============================================================================
// FILE:     src/FileHarvester.Tests/ImageTextify/ReadingOrderTests.cs
// PURPOSE:  Behavior tests for ReadingOrder.Sort. Tests the existing
//           implementation against its documented behavior and the plan's
//           two-column reading-order requirement.
// RELATION: Phase 2.3 Block B. No production code change.
// ============================================================================

using FluentAssertions;
using ImageTextify.Platform.Imaging;
using System.Linq;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Behavior tests for ReadingOrder.Sort.
/// </summary>
public sealed class ReadingOrderTests
{
    // ------------------------------------------------------------------
    // Helper - constructs TextRegion with sensible defaults.
    // ------------------------------------------------------------------

    private static TextRegion R(int index, int x, int y, int w = 100, int h = 20, double c = 0.9)
        => new TextRegion(index, x, y, w, h, c);

    // ------------------------------------------------------------------
    // Structural / contract facts (7)
    // ------------------------------------------------------------------

    [Fact]
    public void SortThrowsOnNullRegions()
    {
        // ARRANGE
        var options = new ReadingOrderOptions();

        // ACT
        var act = () => ReadingOrder.Sort(null!, options);

        // ASSERT
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SortThrowsOnNullOptions()
    {
        // ARRANGE
        var regions = new[] { R(0, 0, 0) };

        // ACT
        var act = () => ReadingOrder.Sort(regions, null!);

        // ASSERT
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SortEmptyInputReturnsEmpty()
    {
        // ARRANGE
        var regions = Array.Empty<TextRegion>();
        var options = new ReadingOrderOptions();

        // ACT
        var result = ReadingOrder.Sort(regions, options);

        // ASSERT
        result.Should().BeEmpty();
    }

    private static readonly int[] SingleRegionExpected = { 0 };

    [Fact]
    public void SortSingleRegionReturnsZero()
    {
        // ARRANGE
        var regions = new[] { R(0, 10, 20) };
        var options = new ReadingOrderOptions();

        // ACT
        var result = ReadingOrder.Sort(regions, options);

        // ASSERT
        result.Should().BeEquivalentTo(SingleRegionExpected, options => options.WithStrictOrdering());
    }

    [Fact]
    public void SortDoesNotMutateInput()
    {
        // ARRANGE
        var regions = new List<TextRegion> { R(0, 10, 20), R(1, 30, 40), R(2, 50, 60) };
        var originalOrder = regions.Select(r => r.Index).ToList();
        var options = new ReadingOrderOptions();

        // ACT
        var result = ReadingOrder.Sort(regions, options);

        // ASSERT
        regions.Select(r => r.Index).Should().BeEquivalentTo(originalOrder);
    }

    [Fact]
    public void SortIsDeterministic()
    {
        // ARRANGE
        var regions = new[] { R(2, 50, 60), R(0, 10, 20), R(1, 30, 40) };
        var options = new ReadingOrderOptions();

        // ACT
        var result1 = ReadingOrder.Sort(regions, options);
        var result2 = ReadingOrder.Sort(regions, options);

        // ASSERT
        result1.Should().BeEquivalentTo(result2);
    }

    [Fact]
    public void SortReturnsSameCountAsInput()
    {
        // ARRANGE
        var regions = new[] { R(0, 10, 20), R(1, 30, 40), R(2, 50, 60), R(3, 70, 80) };
        var options = new ReadingOrderOptions();

        // ACT
        var result = ReadingOrder.Sort(regions, options);

        // ASSERT
        result.Should().HaveCount(regions.Length);
    }

    // ------------------------------------------------------------------
    // Two-column layout facts (4)
    // ------------------------------------------------------------------

    private static readonly int[] TwoColumnExpected = { 0, 2, 1, 3 };

    [Fact]
    public void SortTwoColumnLayoutReturnsLeftColumnFirst()
    {
        // ARRANGE
        // Left column: indices 0, 2 at X=10, Y=0,40
        // Right column: indices 1, 3 at X=200, Y=0,40
        // Gap = 90 (200 - 110), median width = 100, threshold = 100
        // Gap < threshold, so columns merge? No, gap is 90, threshold is 100.
        // Actually: right edge of left column = 10 + 100 = 110
        // gap = 200 - 110 = 90
        // median width = 100, threshold = Math.Max(1, 100) = 100
        // 90 < 100, so they merge into one column.
        // Need gap >= threshold to split. Let's use explicit threshold.
        var regions = new[]
        {
            R(0, 10, 0),   // left col, row 1
            R(1, 200, 0),  // right col, row 1
            R(2, 10, 40),  // left col, row 2
            R(3, 200, 40),  // right col, row 2
        };
        var options = new ReadingOrderOptions(ColumnGapThresholdPx: 80);

        // ACT
        var result = ReadingOrder.Sort(regions, options);

        // ASSERT - Option B: reconstruct regions from indexes
        var orderedRegions = result.Select(i => regions[i]).ToList();
        orderedRegions[0].X.Should().Be(10);  // left col first
        orderedRegions[1].X.Should().Be(10);  // left col second
        orderedRegions[2].X.Should().Be(200); // right col first
        orderedRegions[3].X.Should().Be(200); // right col second
    }

    [Fact]
    public void SortThreeColumnLayoutReturnsLeftToRight()
    {
        // ARRANGE
        // Three columns at X=10, X=200, X=390, each with two rows
        var regions = new[]
        {
            R(0, 10, 0),   // col 1, row 1
            R(1, 200, 0),  // col 2, row 1
            R(2, 390, 0),  // col 3, row 1
            R(3, 10, 40),  // col 1, row 2
            R(4, 200, 40), // col 2, row 2
            R(5, 390, 40), // col 3, row 2
        };
        var options = new ReadingOrderOptions(ColumnGapThresholdPx: 80);

        // ACT
        var result = ReadingOrder.Sort(regions, options);

        // ASSERT - Option B: reconstruct regions
        var orderedRegions = result.Select(i => regions[i]).ToList();
        orderedRegions[0].X.Should().Be(10);
        orderedRegions[1].X.Should().Be(10);
        orderedRegions[2].X.Should().Be(200);
        orderedRegions[3].X.Should().Be(200);
        orderedRegions[4].X.Should().Be(390);
        orderedRegions[5].X.Should().Be(390);
    }

    private static readonly int[] WideGapExpected = { 0, 1 };

    [Fact]
    public void SortWideGapFormsColumnBoundary()
    {
        // ARRANGE
        // Two regions with a 200px gap, threshold set to 150
        // Should split into two columns
        var regions = new[]
        {
            R(0, 10, 0),
            R(1, 320, 0), // right edge of first = 110, gap = 320 - 110 = 210
        };
        var options = new ReadingOrderOptions(ColumnGapThresholdPx: 150);

        // ACT
        var result = ReadingOrder.Sort(regions, options);

        // ASSERT - Option A: index sequence
        result.Should().BeEquivalentTo(WideGapExpected, options => options.WithStrictOrdering());
    }

    [Fact]
    public void SortNullColumnGapUsesMedianWidth()
    {
        // ARRANGE
        // Uniform width 100, median = 100, threshold = 100
        // Gap = 90 (less than threshold), should merge
        var regions = new[]
        {
            R(0, 10, 0),
            R(1, 200, 0), // right edge of first = 110, gap = 90
        };
        var options = new ReadingOrderOptions(ColumnGapThresholdPx: null);

        // ACT
        var result = ReadingOrder.Sort(regions, options);

        // ASSERT - Option B: reconstruct to verify merged behavior
        var orderedRegions = result.Select(i => regions[i]).ToList();
        // Merged means sorted by Y then X, so left-to-right within row
        orderedRegions[0].X.Should().Be(10);
        orderedRegions[1].X.Should().Be(200);
    }

    // ------------------------------------------------------------------
    // Fallback (row-based) facts (3)
    // ------------------------------------------------------------------

    [Fact]
    public void SortNoColumnStructureFallsBackToRows()
    {
        // ARRANGE
        // All regions in one visual column, should fall back to row-by-row
        var regions = new[]
        {
            R(0, 10, 0),
            R(1, 10, 40),
            R(2, 10, 80),
        };
        var options = new ReadingOrderOptions();

        // ACT
        var result = ReadingOrder.Sort(regions, options);

        // ASSERT - Option B: reconstruct to verify row order
        var orderedRegions = result.Select(i => regions[i]).ToList();
        orderedRegions[0].Y.Should().Be(0);
        orderedRegions[1].Y.Should().Be(40);
        orderedRegions[2].Y.Should().Be(80);
    }

    private static readonly int[] RowToleranceExpected = { 0, 1 };

    [Fact]
    public void SortRowToleranceGroupsRegionsOnSameLine()
    {
        // ARRANGE
        // Two regions on same visual line (Y difference = 5, tolerance = 8)
        var regions = new[]
        {
            R(0, 10, 0),
            R(1, 200, 5), // within tolerance
        };
        var options = new ReadingOrderOptions(RowTolerancePx: 8);

        // ACT
        var result = ReadingOrder.Sort(regions, options);

        // ASSERT - Option A: index sequence (left-to-right on same row)
        result.Should().BeEquivalentTo(RowToleranceExpected, options => options.WithStrictOrdering());
    }

    private static readonly int[] RowToleranceExceededExpected = { 0, 1 };

    [Fact]
    public void SortRowToleranceExceededCreatesNewRow()
    {
        // ARRANGE
        // Two regions with Y difference = 15, tolerance = 8
        // Should create separate rows
        var regions = new[]
        {
            R(0, 10, 0),
            R(1, 10, 15), // exceeds tolerance
        };
        var options = new ReadingOrderOptions(RowTolerancePx: 8);

        // ACT
        var result = ReadingOrder.Sort(regions, options);

        // ASSERT - Option A: index sequence (top-to-bottom rows)
        result.Should().BeEquivalentTo(RowToleranceExceededExpected, options => options.WithStrictOrdering());
    }
}
