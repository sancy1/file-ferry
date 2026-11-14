// filepath: src/FileHarvester.Tests/ImageTextify/QwenHandwritingRecognizerTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 2.4d
// purpose:  Contract tests for QwenHandwritingRecognizer. Verifies the
//           prompt passed to the vision service, output cleaning, and
//           error behavior. Uses FakeQwenVisionService.
//
// RELATIONSHIPS
//   Implements : n/a (test class)
//   Depends on : QwenHandwritingRecognizer, FakeQwenVisionService
//   Used by    : xUnit test runner
//   See also   : QwenHandwritingRecognizer.cs

using System;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using ImageTextify.Platform.Imaging;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Contract tests for <see cref="QwenHandwritingRecognizer"/>.
/// </summary>
public class QwenHandwritingRecognizerTests
{
    private static ReadOnlyMemory<byte> SampleBytes()
        => new byte[] { 1, 2, 3, 4 };

    /// <summary>
    /// The recognizer must pass the exact handwriting prompt to the
    /// vision service. Verifies the prompt constant reaches Qwen unmodified.
    /// </summary>
    [Fact]
    public async Task RecognizeAsyncPassesHandwritingPrompt()
    {
        var fake = new FakeQwenVisionService { OutputToReturn = "hello" };
        var recognizer = new QwenHandwritingRecognizer(fake);

        await recognizer.RecognizeAsync(SampleBytes());

        fake.CallCount.Should().Be(1);
        fake.PromptsReceived[0].Should().Be(QwenHandwritingRecognizer.HandwritingPrompt);
    }

    /// <summary>
    /// Output containing the Qwen chat end token must be cleaned.
    /// </summary>
    [Fact]
    public async Task RecognizeAsyncStripsImEndToken()
    {
        var fake = new FakeQwenVisionService { OutputToReturn = "hello<|im_end|>" };
        var recognizer = new QwenHandwritingRecognizer(fake);

        var result = await recognizer.RecognizeAsync(SampleBytes());

        result.Should().Be("hello");
    }

    /// <summary>
    /// Output containing the endoftext token must be cleaned.
    /// </summary>
    [Fact]
    public async Task RecognizeAsyncStripsEndOfTextToken()
    {
        var fake = new FakeQwenVisionService { OutputToReturn = "hello<|endoftext|>" };
        var recognizer = new QwenHandwritingRecognizer(fake);

        var result = await recognizer.RecognizeAsync(SampleBytes());

        result.Should().Be("hello");
    }

    /// <summary>
    /// Output containing the im_start token must be cleaned.
    /// </summary>
    [Fact]
    public async Task RecognizeAsyncStripsImStartToken()
    {
        var fake = new FakeQwenVisionService { OutputToReturn = "hello<|im_start|>" };
        var recognizer = new QwenHandwritingRecognizer(fake);

        var result = await recognizer.RecognizeAsync(SampleBytes());

        result.Should().Be("hello");
    }

    /// <summary>
    /// Empty model output must be returned as empty string, not null.
    /// </summary>
    [Fact]
    public async Task RecognizeAsyncReturnsEmptyStringForEmptyOutput()
    {
        var fake = new FakeQwenVisionService { OutputToReturn = "" };
        var recognizer = new QwenHandwritingRecognizer(fake);

        var result = await recognizer.RecognizeAsync(SampleBytes());

        result.Should().Be("");
    }

    /// <summary>
    /// Outer whitespace must be trimmed. Inner whitespace and newlines
    /// must be preserved (they carry the handwriting layout).
    /// </summary>
    [Fact]
    public async Task RecognizeAsyncTrimsOuterWhitespaceOnly()
    {
        var fake = new FakeQwenVisionService { OutputToReturn = "\n  hello world  \n" };
        var recognizer = new QwenHandwritingRecognizer(fake);

        var result = await recognizer.RecognizeAsync(SampleBytes());

        result.Should().Be("hello world");
    }

    /// <summary>
    /// Empty crop bytes must throw ArgumentException before calling Qwen.
    /// </summary>
    [Fact]
    public async Task RecognizeAsyncThrowsForEmptyCropBytes()
    {
        var fake = new FakeQwenVisionService();
        var recognizer = new QwenHandwritingRecognizer(fake);

        var act = async () => await recognizer.RecognizeAsync(ReadOnlyMemory<byte>.Empty);

        await act.Should().ThrowAsync<ArgumentException>();
        fake.CallCount.Should().Be(0);
    }

    /// <summary>
    /// Null qwen service must throw ArgumentNullException on construction.
    /// </summary>
    [Fact]
    public void ConstructorThrowsForNullQwen()
    {
        var act = () => new QwenHandwritingRecognizer(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}

