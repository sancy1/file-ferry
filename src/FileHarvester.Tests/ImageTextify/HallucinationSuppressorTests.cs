// filepath: src/FileHarvester.Tests/ImageTextify/HallucinationSuppressorTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 5 Block 5.D.3
// purpose:  Behavior tests for HallucinationSuppressor. Pure logic; no
//           model, no process, no external dependency.
//
// RELATIONSHIPS
//   Implements : n/a (test file)
//   Depends on : HallucinationSuppressor
//   Used by    : dotnet test --filter HallucinationSuppressorTests
//   See also   : ImageTextify.Platform.Speech.HallucinationSuppressor

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ImageTextify.Core.Abstractions;
using ImageTextify.Platform.Speech;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

public sealed class HallucinationSuppressorTests
{
    private static RecognizedSegment Seg(double startSec, double endSec, string text, double prob) =>
        new(TimeSpan.FromSeconds(startSec), TimeSpan.FromSeconds(endSec), text, prob, "en");

    private static SpeechRegion Region(double startSec, double endSec) =>
        new(TimeSpan.FromSeconds(startSec), TimeSpan.FromSeconds(endSec));

    [Fact]
    public async Task SuppressorKeepsAllSegmentsWhenAllWithinSpeech()
    {
        var segments = new List<RecognizedSegment>
        {
            Seg(0, 1, "hello", 0.9),
            Seg(1, 2, "world", 0.9),
            Seg(2, 3, "again", 0.9),
        };
        var regions = new List<SpeechRegion> { Region(0, 4) };

        var result = await new HallucinationSuppressor().SuppressAsync(segments, regions, CancellationToken.None);

        result.Segments.Should().HaveCount(3);
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task SuppressorDropsSegmentsOutsideSpeech()
    {
        var segments = new List<RecognizedSegment>
        {
            Seg(0, 1, "hello", 0.9),
            Seg(5, 6, "outside", 0.9),
        };
        var regions = new List<SpeechRegion> { Region(0, 2) };

        var result = await new HallucinationSuppressor().SuppressAsync(segments, regions, CancellationToken.None);

        result.Segments.Should().HaveCount(1);
        result.Segments[0].Text.Should().Be("hello");
        result.Warnings.Should().Contain(w => w.Code == "suppressed-outside-speech");
    }

    [Fact]
    public async Task SuppressorDropsRepetitionLoops()
    {
        var segments = new List<RecognizedSegment>
        {
            Seg(0, 1, "loop", 0.9),
            Seg(1, 2, "loop", 0.9),
            Seg(2, 3, "loop", 0.9),
            Seg(3, 4, "loop", 0.9),
        };
        var regions = new List<SpeechRegion> { Region(0, 5) };

        var result = await new HallucinationSuppressor().SuppressAsync(segments, regions, CancellationToken.None);

        // Run length 3 means: 1st kept, 2nd kept (run=2 < 3), 3rd dropped (run=3), 4th dropped.
        // Actually with threshold=3: run=1 kept, run=2 kept, run=3 dropped, run=4 dropped.
        result.Segments.Should().HaveCount(2);
        result.Warnings.Should().Contain(w => w.Code == "suppressed-repetition");
    }

    [Fact]
    public async Task SuppressorDropsKnownHallucinationPhraseOutsideSpeech()
    {
        var segments = new List<RecognizedSegment>
        {
            Seg(0, 1, "real speech", 0.9),
            Seg(5, 6, "thanks for watching", 0.9),
        };
        var regions = new List<SpeechRegion> { Region(0, 2) };

        var result = await new HallucinationSuppressor().SuppressAsync(segments, regions, CancellationToken.None);

        result.Segments.Should().HaveCount(1);
        result.Segments[0].Text.Should().Be("real speech");
    }

    [Fact]
    public async Task SuppressorDropsKnownHallucinationPhraseWithLowProbability()
    {
        var segments = new List<RecognizedSegment>
        {
            Seg(0, 1, "real speech", 0.9),
            Seg(1, 2, "thanks for watching", 0.1),
        };
        var regions = new List<SpeechRegion> { Region(0, 3) };

        var result = await new HallucinationSuppressor().SuppressAsync(segments, regions, CancellationToken.None);

        result.Segments.Should().HaveCount(1);
        result.Segments[0].Text.Should().Be("real speech");
        result.Warnings.Should().Contain(w => w.Code == "suppressed-known-phrase");
    }

    [Fact]
    public async Task SuppressorKeepsKnownPhraseInsideSpeechWithHighProbability()
    {
        // "thank you" is real speech when it is inside a speech region
        // with high confidence. The suppressor must not drop it.
        var segments = new List<RecognizedSegment>
        {
            Seg(0, 1, "thank you", 0.9),
        };
        var regions = new List<SpeechRegion> { Region(0, 2) };

        var result = await new HallucinationSuppressor().SuppressAsync(segments, regions, CancellationToken.None);

        result.Segments.Should().HaveCount(1);
        result.Segments[0].Text.Should().Be("thank you");
        result.Warnings.Should().BeEmpty();
    }
}