// ============================================================================
// FILE:     src/FileHarvester.Tests/ImageTextify/CoreContractsTests.cs
// PURPOSE:  Structural tests for the ImageTextify.Core contracts.
//           Asserts enum shape, record construction, redaction
//           behavior, contract method shapes, OCR defaults, and the
//           OcrTextKind members.
// RELATION: Exercises the ImageTextify.Core types declared in Phase
//           1.3. Phase 1.3 exit-criteria test.
// ============================================================================

using System.Reflection;
using FluentAssertions;
using ImageTextify.Core.Abstractions;
using ImageTextify.Core.Diagnostics;
using ImageTextify.Core.Domain;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Structural tests for the ImageTextify.Core contracts.
/// </summary>
public sealed class CoreContractsTests
{
    private static readonly string[] ExpectedExtractionKindNames =
        new[] { "Image", "Pdf", "Audio", "Video" };

    private static readonly string[] ExpectedOcrTextKindNames =
        new[] { "Printed", "Handwritten", "Mixed", "Unknown" };
    // -----------------------------------------------------------------
    // ExtractionKind
    // -----------------------------------------------------------------

    [Fact]
    public void ExtractionKindHasExactlyFourMembers()
    {
        var names = Enum.GetNames<ExtractionKind>();
        names.Should().HaveCount(4);
        names.Should().BeEquivalentTo(ExpectedExtractionKindNames);
    }

    [Fact]
    public void ExtractionKindHasNoDuplicateValues()
    {
        var values = Enum.GetValues<ExtractionKind>().Cast<int>().ToArray();
        values.Should().OnlyHaveUniqueItems();
    }

    // -----------------------------------------------------------------
    // ImageTextifySourceFile
    // -----------------------------------------------------------------

    [Fact]
    public void ImageTextifySourceFilePreservesConstructorValues()
    {
        var file = new ImageTextifySourceFile(
            "C:\\x\\a.png",
            "a.png",
            ExtractionKind.Image,
            123,
            null);

        file.FullPath.Should().Be("C:\\x\\a.png");
        file.DisplayName.Should().Be("a.png");
        file.Kind.Should().Be(ExtractionKind.Image);
        file.SizeBytes.Should().Be(123);
        file.Duration.Should().BeNull();
    }

    [Fact]
    public void ImageTextifySourceFileAllowsNullDurationForImage()
    {
        var file = new ImageTextifySourceFile(
            "C:\\x\\a.png", "a.png", ExtractionKind.Image, 1, null);

        file.Duration.Should().BeNull();
    }

    [Fact]
    public void ImageTextifySourceFileAllowsNullDurationForPdf()
    {
        var file = new ImageTextifySourceFile(
            "C:\\x\\a.pdf", "a.pdf", ExtractionKind.Pdf, 1, null);

        file.Duration.Should().BeNull();
    }

    // -----------------------------------------------------------------
    // ImageTextifyResult
    // -----------------------------------------------------------------

    [Fact]
    public void ImageTextifyResultPreservesWarningsAsEmptyListWhenEmptyPassed()
    {
        var src = new ImageTextifySourceFile(
            "C:\\x\\a.png", "a.png", ExtractionKind.Image, 1, null);

        var result = new ImageTextifyResult(
            src,
            "text",
            1,
            null,
            null,
            Array.Empty<string>(),
            TimeSpan.FromMilliseconds(5),
            "test-engine",
            null);

        result.Warnings.Should().NotBeNull();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void ImageTextifyResultRoundTripsTimestampsForAudio()
    {
        var src = new ImageTextifySourceFile(
            "C:\\x\\a.wav", "a.wav", ExtractionKind.Audio, 1, TimeSpan.FromSeconds(2));

        var stamps = new[] { TimeSpan.Zero, TimeSpan.FromSeconds(1) };
        var result = new ImageTextifyResult(
            src,
            "hi",
            2,
            stamps,
            0.9,
            Array.Empty<string>(),
            TimeSpan.FromMilliseconds(10),
            "whisper-net",
            "small");

        result.Timestamps.Should().NotBeNull();
        result.Timestamps!.Should().HaveCount(2);
    }

    [Fact]
    public void ImageTextifyResultCarriesNullTimestampsForImage()
    {
        var src = new ImageTextifySourceFile(
            "C:\\x\\a.png", "a.png", ExtractionKind.Image, 1, null);

        var result = new ImageTextifyResult(
            src,
            "text",
            1,
            null,
            0.5,
            Array.Empty<string>(),
            TimeSpan.Zero,
            "onnx-paddleocr",
            "v1");

        result.Timestamps.Should().BeNull();
    }

    // -----------------------------------------------------------------
    // ImageTextifyDocument
    // -----------------------------------------------------------------

    [Fact]
    public void ImageTextifyDocumentPreservesSchemaVersionAndItems()
    {
        var doc = new ImageTextifyDocument(
            Array.Empty<ImageTextifyResult>(),
            ImageTextifyDocument.CurrentSchemaVersion);

        doc.SchemaVersion.Should().Be("1.0");
        doc.Items.Should().NotBeNull();
        doc.Items.Should().BeEmpty();
    }

    // -----------------------------------------------------------------
    // ImageTextifyRedaction
    // -----------------------------------------------------------------

    [Fact]
    public void ImageTextifyRedactionRedactsWindowsUserPath()
    {
        var result = ImageTextifyRedaction.RedactPath(
            "C:\\Users\\HP\\Desktop\\a.png");

        result.Should().Be("C:\\Users\\<user>\\Desktop\\a.png");
    }

    [Fact]
    public void ImageTextifyRedactionRedactsPosixHomePath()
    {
        var result = ImageTextifyRedaction.RedactPath(
            "/home/alice/docs/a.png");

        result.Should().Be("/home/<user>/docs/a.png");
    }

    [Fact]
    public void ImageTextifyRedactionStripsUriQueryAndUserInfo()
    {
        var result = ImageTextifyRedaction.RedactUri(
            "https://alice:secret@example.com/model?token=abc");

        result.Should().Be("https://example.com/model");
    }

    [Fact]
    public void ImageTextifyRedactionReturnsEmptyForNull()
    {
        ImageTextifyRedaction.RedactPath(null).Should().BeEmpty();
        ImageTextifyRedaction.RedactUri(null).Should().BeEmpty();
    }

    // -----------------------------------------------------------------
    // Contract shapes
    // -----------------------------------------------------------------

    [Fact]
    public void IImageOcrEngineExposesExpectedShape()
    {
        var method = typeof(IImageOcrEngine).GetMethod("ExtractAsync");
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be<Task<OcrResult>>();
        method.GetParameters().Should().HaveCount(3);
    }

    [Fact]
    public void IPdfTextExtractorExposesExpectedShape()
    {
        var method = typeof(IPdfTextExtractor).GetMethod("ExtractAsync");
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be<Task<PdfTextResult>>();
        method.GetParameters().Should().HaveCount(3);
    }

    [Fact]
    public void IMediaPreparerExposesExpectedShape()
    {
        typeof(IMediaPreparer).GetMethod("ProbeAsync").Should().NotBeNull();
        typeof(IMediaPreparer).GetMethod("ExtractAudioAsync").Should().NotBeNull();
    }

    [Fact]
    public void ISpeechTranscriberExposesExpectedShape()
    {
        var method = typeof(ISpeechTranscriber).GetMethod("TranscribeAsync");
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be<Task<TranscriptResult>>();
    }

    [Fact]
    public void ITextCleanerExposesExpectedShape()
    {
        var method = typeof(ITextCleaner).GetMethod("Clean");
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be<string>();
        method.GetParameters().Should().HaveCount(1);
    }

    [Fact]
    public void ICameraCaptureServiceExposesExpectedShape()
    {
        typeof(ICameraCaptureService).GetMethod("EnumerateAsync").Should().NotBeNull();
        typeof(ICameraCaptureService).GetMethod("StartAsync").Should().NotBeNull();
    }

    [Fact]
    public void ILiveTranscriptionServiceExposesExpectedShape()
    {
        typeof(ILiveTranscriptionService).GetMethod("StartAsync").Should().NotBeNull();
    }

    [Fact]
    public void IAudioCaptureServiceExposesExpectedShape()
    {
        typeof(IAudioCaptureService).GetMethod("StartAsync").Should().NotBeNull();
    }

    [Fact]
    public void IAudioEnhancerExposesExpectedShape()
    {
        var method = typeof(IAudioEnhancer).GetMethod("EnhanceAsync");
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be<Task<Stream>>();
    }

    // -----------------------------------------------------------------
    // OCR amendments
    // -----------------------------------------------------------------

    [Fact]
    public void ImageOcrOptionsDefaultsToEnhancementAndOrientationEnabled()
    {
        var options = new ImageOcrOptions();
        options.Enhance.Should().BeTrue();
        options.DetectOrientation.Should().BeTrue();
    }

    [Fact]
    public void ImageOcrOptionsDefaultsHandwritingRecognitionToFalse()
    {
        var options = new ImageOcrOptions();
        options.AttemptHandwritingRecognition.Should().BeFalse();
    }

    [Fact]
    public void OcrTextKindHasExpectedMembers()
    {
        var names = Enum.GetNames<OcrTextKind>();
        names.Should().BeEquivalentTo(ExpectedOcrTextKindNames);
    }
}
