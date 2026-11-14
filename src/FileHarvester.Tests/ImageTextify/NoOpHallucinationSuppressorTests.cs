// filepath: src/FileHarvester.Tests/ImageTextify/NoOpHallucinationSuppressorTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 5 Block 5.D.3
// purpose:  Verifies that NoOpHallucinationSuppressor returns segments
//           unchanged and produces no warnings.
//
// RELATIONSHIPS
//   Implements : n/a (test file)
//   Depends on : NoOpHallucinationSuppressor
//   Used by    : dotnet test --filter NoOpHallucinationSuppressorTests
//   See also   : ImageTextify.Platform.Speech.NoOpHallucinationSuppressor

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ImageTextify.Core.Abstractions;
using ImageTextify.Platform.Speech;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

public sealed class NoOpHallucinationSuppressorTests
{
    [Fact]
    public async Task NoOpReturnsSegmentsUnchangedAndNoWarnings()
    {
        var segments = new List<RecognizedSegment>
        {
            new(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(1), "hello", 0.9, "en"),
            new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "world", 0.9, "en"),
        };
        var regions = new List<SpeechRegion>
        {
            new(TimeSpan.Zero, TimeSpan.FromSeconds(3)),
        };
        var suppressor = new NoOpHallucinationSuppressor();

        var result = await suppressor.SuppressAsync(segments, regions, CancellationToken.None);

        result.Segments.Should().BeSameAs(segments);
        result.Warnings.Should().BeEmpty();
    }
}