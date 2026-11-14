// filepath: src/FileHarvester.Tests/ImageTextify/QwenPrintedFallbackRecognizerTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 2.4e
// purpose:  Contract tests for QwenPrintedFallbackRecognizer.
//
// RELATIONSHIPS
//   Implements : n/a
//   Depends on : QwenPrintedFallbackRecognizer, FakeQwenVisionService
//   Used by    : dotnet test --filter QwenPrintedFallbackRecognizerTests
//   See also   : QwenHandwritingRecognizerTests.cs
//
// NOTES
//   - Uses FakeQwenVisionService (generic IQwenVisionService fake).
//   - Eight tests cover: null guard, empty-input guard, empty output,
//     length-proxy rejection, length-proxy accept, prompt identity,
//     cancellation propagation, chat-template stripping.

using System;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Platform.Imaging;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Contract tests for <see cref="QwenPrintedFallbackRecognizer"/>.
/// </summary>
public sealed class QwenPrintedFallbackRecognizerTests
{
    private static readonly byte[] NonEmptyCrop = new byte[] { 1, 2, 3, 4 };

    /// <summary>
    /// The constructor must reject a null vision service.
    /// </summary>
    [Fact]
    public void ConstructorThrowsWhenVisionIsNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => new QwenPrintedFallbackRecognizer(null!));
    }

    /// <summary>
    /// An empty crop must be rejected before any Qwen call is attempted.
    /// </summary>
    [Fact]
    public async Task RecognizeAsyncThrowsWhenCropBytesEmpty()
    {
        var fake = new FakeQwenVisionService();
        var sut = new QwenPrintedFallbackRecognizer(fake);

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.RecognizeAsync(ReadOnlyMemory<byte>.Empty, "x"));
        Assert.Equal(0, fake.CallCount);
    }

    /// <summary>
    /// An empty Qwen output must be rejected (return empty string).
    /// </summary>
    [Fact]
    public async Task RecognizeAsyncReturnsEmptyWhenQwenReturnsEmpty()
    {
        var fake = new FakeQwenVisionService { OutputToReturn = string.Empty };
        var sut = new QwenPrintedFallbackRecognizer(fake);

        var result = await sut.RecognizeAsync(NonEmptyCrop, "x");
        Assert.Equal(string.Empty, result);
    }

    /// <summary>
    /// When the cleaned Qwen output is not strictly longer than the
    /// PP-OCR text, the result must be rejected (return empty string).
    /// </summary>
    [Fact]
    public async Task RecognizeAsyncReturnsEmptyWhenQwenNotLongerThanPpocr()
    {
        var fake = new FakeQwenVisionService { OutputToReturn = "abc" };
        var sut = new QwenPrintedFallbackRecognizer(fake);

        var result = await sut.RecognizeAsync(NonEmptyCrop, "abcde");
        Assert.Equal(string.Empty, result);
    }

    /// <summary>
    /// When the cleaned Qwen output is strictly longer than the PP-OCR
    /// text, it must be accepted and returned.
    /// </summary>
    [Fact]
    public async Task RecognizeAsyncReturnsQwenTextWhenStrictlyLonger()
    {
        var fake = new FakeQwenVisionService { OutputToReturn = "Hello, world." };
        var sut = new QwenPrintedFallbackRecognizer(fake);

        var result = await sut.RecognizeAsync(NonEmptyCrop, "Hello,");
        Assert.Equal("Hello, world.", result);
    }

    /// <summary>
    /// The exact printed prompt must be passed to GenerateAsync.
    /// </summary>
    [Fact]
    public async Task RecognizeAsyncPassesPrintedPrompt()
    {
        var fake = new FakeQwenVisionService { OutputToReturn = "longer text here" };
        var sut = new QwenPrintedFallbackRecognizer(fake);

        await sut.RecognizeAsync(NonEmptyCrop, "x");

        Assert.Single(fake.PromptsReceived);
        Assert.Contains("printed text", fake.PromptsReceived[0]);
        Assert.Contains("Do not invent", fake.PromptsReceived[0]);
    }

    /// <summary>
    /// The cancellation token must be forwarded to GenerateAsync.
    /// </summary>
    [Fact]
    public async Task RecognizeAsyncHonorsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var fake = new FakeQwenVisionService { OutputToReturn = "ignored" };
        var sut = new QwenPrintedFallbackRecognizer(fake);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.RecognizeAsync(NonEmptyCrop, "x", cts.Token));
    }

    /// <summary>
    /// Chat-template tokens in the model output must be stripped.
    /// </summary>
    [Fact]
    public async Task RecognizeAsyncStripsChatTemplateTokens()
    {
        var fake = new FakeQwenVisionService
        {
            OutputToReturn = "  Cleaned text<|im_end|><|endoftext|>  "
        };
        var sut = new QwenPrintedFallbackRecognizer(fake);

        var result = await sut.RecognizeAsync(NonEmptyCrop, "x");
        Assert.Equal("Cleaned text", result);
    }
}
