// filepath: src/FileHarvester.Tests/ImageTextify/StreamingSileroSegmenterTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 10 Block 10.B.4
// purpose:  Proves the StreamingSileroSegmenter state machine fires the
//           correct UtteranceStarted / UtteranceEnded events under BOTH
//           possible DetectSpeechNoResetAsync semantics (growing list, or
//           new-segments-only), and fires nothing during silence.
// RELATIONSHIPS
//   Implements : n/a (test class)
//   Depends on : StreamingSileroSegmenter (internal ctor),
//                FakeVadSession, SyntheticAudioStream, VadSegmentData
//   Used by    : dotnet test --filter StreamingSileroSegmenterTests
//   See also   : ImageTextify.Platform.Audio.StreamingSileroSegmenter
// NOTES
//   - The real Silero VAD rejects synthetic silence and sine (proven by
//     VadNoResetSemanticsDiagnostic, 2026-11-11T02:49:14), so the real
//     WhisperVadProcessor cannot drive these tests. FakeVadSession is the
//     substitution point exposed by the internal ctor.
//   - ITX-10.B-017 records that the exact semantics of
//     DetectSpeechNoResetAsync were not measured by the Phase 10.B.2 probe.
//     These tests prove the segmenter's behavior is correct under either
//     semantics; Gate 10.3 confirms against real speech.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ImageTextify.Core.Abstractions;
using ImageTextify.Platform.Audio;
using Whisper.net;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

public sealed class StreamingSileroSegmenterTests
{
    private const int ChunkSamples = 1600;
    private const int ChunkBytes = ChunkSamples * 4;

    private static VadSegmentData Seg(double startSec, double endSec) =>
        new(TimeSpan.FromSeconds(startSec), TimeSpan.FromSeconds(endSec));

    private static IReadOnlyList<VadSegmentData> Empty => Array.Empty<VadSegmentData>();

    private static VadSegmentData[] One(double s, double e) =>
        new[] { Seg(s, e) };

    private static VadSegmentData[] Two(double s1, double e1, double s2, double e2) =>
        new[] { Seg(s1, e1), Seg(s2, e2) };

    [Fact]
    public async Task GrowingListSemanticsFiresOneStartedAndOneEnded()
    {
        // Fake returns the accumulated list per call, growing as speech continues.
        IReadOnlyList<VadSegmentData>[] script =
        {
            Empty, Empty,
            One(0, 1),
            Two(0, 1, 0, 2),
            Two(0, 2, 0, 3),
        };
        var fake = new FakeVadSession().Script(script);
        var audio = new SyntheticAudioStream(ChunkBytes, 55);

        int started = 0; int ended = 0;
        UtteranceEndedEventArgs? captured = null;
        await using (var seg = new StreamingSileroSegmenter(fake, checkEverySamples: 10 * ChunkSamples, vadWindowSamples: int.MaxValue))
        {
            seg.UtteranceStarted += (_, _) => Interlocked.Increment(ref started);
            seg.UtteranceEnded += (_, e) => { Interlocked.Increment(ref ended); captured = e; };
            await seg.RunAsync(audio, CancellationToken.None);
        }

        started.Should().Be(1);
        ended.Should().Be(1);
        captured.Should().NotBeNull();
        captured!.Samples.Length.Should().Be(55 * ChunkSamples);
        captured.End.Should().BeGreaterThan(captured.Start);
        fake.ResetStateCallCount.Should().Be(1);
    }

    [Fact]
    public async Task NewSegmentsOnlySemanticsFiresOneStartedAndOneEnded()
    {
        // Fake returns only the new segment per call, not the accumulated list.
        IReadOnlyList<VadSegmentData>[] script =
        {
            Empty, Empty,
            One(0, 1),
            One(1, 2),
            One(2, 3),
        };
        var fake = new FakeVadSession().Script(script);
        var audio = new SyntheticAudioStream(ChunkBytes, 55);

        int started = 0; int ended = 0;
        UtteranceEndedEventArgs? captured = null;
        await using (var seg = new StreamingSileroSegmenter(fake, checkEverySamples: 10 * ChunkSamples, vadWindowSamples: int.MaxValue))
        {
            seg.UtteranceStarted += (_, _) => Interlocked.Increment(ref started);
            seg.UtteranceEnded += (_, e) => { Interlocked.Increment(ref ended); captured = e; };
            await seg.RunAsync(audio, CancellationToken.None);
        }

        started.Should().Be(1);
        ended.Should().Be(1);
        captured.Should().NotBeNull();
        captured!.Samples.Length.Should().Be(55 * ChunkSamples);
        captured.End.Should().BeGreaterThan(captured.Start);
        fake.ResetStateCallCount.Should().Be(1);
    }

    [Fact]
    public async Task AllSilenceFiresNoEvents()
    {
        var fake = new FakeVadSession();
        var audio = new SyntheticAudioStream(ChunkBytes, 55);

        int started = 0; int ended = 0;
        await using (var seg = new StreamingSileroSegmenter(fake, checkEverySamples: 10 * ChunkSamples, vadWindowSamples: int.MaxValue))
        {
            seg.UtteranceStarted += (_, _) => Interlocked.Increment(ref started);
            seg.UtteranceEnded += (_, _) => Interlocked.Increment(ref ended);
            await seg.RunAsync(audio, CancellationToken.None);
        }

        started.Should().Be(0);
        ended.Should().Be(0);
        fake.ResetStateCallCount.Should().Be(0);
    }
}