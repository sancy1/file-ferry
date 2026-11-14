// filepath: src/FileHarvester.Tests/ImageTextify/OcrLineRouterTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 2.4b2
// purpose:  Behavior tests for OcrLineRouter. Verifies the six garbage
//           heuristics and the routing decision. Pure logic, no ONNX.
//
// RELATIONSHIPS
//   Implements : n/a (test file)
//   Depends on : ImageTextify.Platform.Imaging.OcrLineRouter
//   Used by    : none found (test runner)
//   See also   : OcrLineRouter.cs, IOcrLineRouter.cs
//
// NOTES
//   - Every fact constructs a fresh OcrLineRouter and calls
//     ShouldRouteToTrocr with specific inputs, asserting the expected
//     boolean.
//   - Fact names describe the heuristic tested.

using FluentAssertions;
using ImageTextify.Platform.Imaging;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Tests for <see cref="OcrLineRouter"/>.
/// </summary>
public sealed class OcrLineRouterTests
{
    private static OcrLineRouter MakeRouter() => new();

    /// <summary>
    /// Normal plausible text with confidence returns false (keep PP-OCR).
    /// </summary>
    [Fact]
    public void PlausibleTextAtHighConfidenceKeepsPpocr()
    {
        var router = MakeRouter();
        var shouldRoute = router.ShouldRouteToTrocr(
            ppocrConfidence: 0.95,
            ppocrText: "Hello world",
            cropWidth: 400,
            cropHeight: 60);
        shouldRoute.Should().BeFalse();
    }

    /// <summary>
    /// Null confidence routes to TrOCR (heuristic 1).
    /// </summary>
    [Fact]
    public void NullConfidenceRoutesToTrocr()
    {
        var router = MakeRouter();
        var shouldRoute = router.ShouldRouteToTrocr(
            ppocrConfidence: null,
            ppocrText: "Hello world",
            cropWidth: 400,
            cropHeight: 60);
        shouldRoute.Should().BeTrue();
    }

    /// <summary>
    /// Empty text routes to TrOCR (heuristic 1 continuation).
    /// </summary>
    [Fact]
    public void EmptyTextRoutesToTrocr()
    {
        var router = MakeRouter();
        var shouldRoute = router.ShouldRouteToTrocr(
            ppocrConfidence: 0.95,
            ppocrText: "",
            cropWidth: 400,
            cropHeight: 60);
        shouldRoute.Should().BeTrue();
    }

    /// <summary>
    /// Whitespace-only text routes to TrOCR.
    /// </summary>
    [Fact]
    public void WhitespaceTextRoutesToTrocr()
    {
        var router = MakeRouter();
        var shouldRoute = router.ShouldRouteToTrocr(
            ppocrConfidence: 0.95,
            ppocrText: "   ",
            cropWidth: 400,
            cropHeight: 60);
        shouldRoute.Should().BeTrue();
    }

    /// <summary>
    /// Repeated-character garbage routes to TrOCR (heuristic 4).
    /// </summary>
    [Fact]
    public void RepeatedCharacterGarbageRoutesToTrocr()
    {
        var router = MakeRouter();
        var shouldRoute = router.ShouldRouteToTrocr(
            ppocrConfidence: 0.95,
            ppocrText: "aaaaaaa",
            cropWidth: 400,
            cropHeight: 60);
        shouldRoute.Should().BeTrue();
    }

    /// <summary>
    /// Text with over 80% non-alphanumeric routes to TrOCR (heuristic 3).
    /// </summary>
    [Fact]
    public void MostlyNonAlphanumericRoutesToTrocr()
    {
        var router = MakeRouter();
        var shouldRoute = router.ShouldRouteToTrocr(
            ppocrConfidence: 0.95,
            ppocrText: "!!!@@@###$$$",
            cropWidth: 400,
            cropHeight: 60);
        shouldRoute.Should().BeTrue();
    }

    /// <summary>
    /// Short text in a narrow crop does NOT route (heuristic 5 tolerance).
    /// </summary>
    [Fact]
    public void ShortTextInNarrowCropKeepsPpocr()
    {
        var router = MakeRouter();
        var shouldRoute = router.ShouldRouteToTrocr(
            ppocrConfidence: 0.95,
            ppocrText: "OK",
            cropWidth: 60,
            cropHeight: 40);
        shouldRoute.Should().BeFalse();
    }

    /// <summary>
    /// Very long text for a narrow crop routes to TrOCR (heuristic 6).
    /// </summary>
    [Fact]
    public void TooLongForCropWidthRoutesToTrocr()
    {
        var router = MakeRouter();
        var longText = new string('x', 300);
        var shouldRoute = router.ShouldRouteToTrocr(
            ppocrConfidence: 0.95,
            ppocrText: longText,
            cropWidth: 200,
            cropHeight: 40);
        shouldRoute.Should().BeTrue();
    }

    /// <summary>
    /// A digit and letter mixture in a wide crop is plausible.
    /// </summary>
    [Fact]
    public void MixedAlphanumericWideCropKeepsPpocr()
    {
        var router = MakeRouter();
        var shouldRoute = router.ShouldRouteToTrocr(
            ppocrConfidence: 0.9,
            ppocrText: "Invoice 12345",
            cropWidth: 400,
            cropHeight: 60);
        shouldRoute.Should().BeFalse();
    }

    /// <summary>
    /// Invalid crop width throws ArgumentException.
    /// </summary>
    [Fact]
    public void ZeroCropWidthThrows()
    {
        var router = MakeRouter();
        var act = () => router.ShouldRouteToTrocr(
            ppocrConfidence: 0.9,
            ppocrText: "hello",
            cropWidth: 0,
            cropHeight: 60);
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Invalid crop height throws ArgumentException.
    /// </summary>
    [Fact]
    public void ZeroCropHeightThrows()
    {
        var router = MakeRouter();
        var act = () => router.ShouldRouteToTrocr(
            ppocrConfidence: 0.9,
            ppocrText: "hello",
            cropWidth: 100,
            cropHeight: 0);
        act.Should().Throw<ArgumentException>();
    }
}