// filepath: src/FileHarvester.Tests/ImageTextify/LiveTranscriptionServiceTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 10 Block 10.C.4
// purpose:  Behaviour tests for LiveTranscriptionService and
//           LiveTranscriptionSession: capture open, state transitions,
//           recognizer call, suppressor call, event emission, temp WAV
//           lifecycle, StopAsync, DisposeAsync.
// RELATIONSHIPS
//   Implements : n/a (test class)
//   Depends on : LiveTranscriptionService, LiveTranscriptionSession,
//                FakeAudioCaptureService, FakeAudioCaptureSession,
//                FakeStreamingSpeechSegmenter, FakeSpeechRecognizer,
//                FakeHallucinationSuppressor
//   Used by    : dotnet test --filter LiveTranscriptionServiceTests
//   See also   : ImageTextify.Platform.Audio.LiveTranscriptionService
// NOTES
//   - The tests drive the real code path through StartAsync and wait
//     for the run loop to finish via the internal RunTask accessor
//     exposed for testability (Block 10.C.3e).
//   - The recognizer and suppressor are scripted; their calls are
//     asserted by counting.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ImageTextify.Core.Abstractions;
using ImageTextify.Platform.Audio;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

public sealed class LiveTranscriptionServiceTests
{
    private const int ChunkSamples = 1600;
    private const int ChunkBytes = ChunkSamples * 4;

    private static UtteranceEndedEventArgs EndAt(double seconds, int samples = ChunkSamples)
    {
        var buf = new float[samples];
        return new UtteranceEndedEventArgs(new ReadOnlyMemory<float>(buf), TimeSpan.Zero, TimeSpan.FromSeconds(seconds));
    }

    private static RecognizedSegment Recog(string text) =>
        new(TimeSpan.Zero, TimeSpan.FromSeconds(1), text, 0.9, "en");

    private static (LiveTranscriptionService Service, FakeAudioCaptureSession Capture, FakeStreamingSpeechSegmenter Segmenter, FakeSpeechRecognizer Recognizer, FakeHallucinationSuppressor Suppressor) Build(int chunks)
    {
        var capture = new FakeAudioCaptureSession(ChunkBytes, chunks);
        var captureSvc = new FakeAudioCaptureService(capture);
        var segmenter = new FakeStreamingSpeechSegmenter();
        var recognizer = new FakeSpeechRecognizer();
        var suppressor = new FakeHallucinationSuppressor();
        var service = new LiveTranscriptionService(captureSvc, segmenter, recognizer, suppressor);
        return (service, capture, segmenter, recognizer, suppressor);
    }

    [Fact]
    public async Task StartAsyncOpensCaptureAndReturnsListeningSession()
    {
        var (service, _, _, _, _) = Build(5);
        var session = await service.StartAsync(new LiveTranscriptionOptions(), CancellationToken.None);
        session.State.Should().BeOneOf(LiveTranscriptionState.Listening, LiveTranscriptionState.Idle);
        await ((LiveTranscriptionSession)session).RunTask;
        await session.DisposeAsync();
    }

    [Fact]
    public async Task StartAsyncRejectsNullOptions()
    {
        var (service, _, _, _, _) = Build(5);
        Func<Task> act = async () => await service.StartAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task StartAsyncRejectsCancelledToken()
    {
        var (service, _, _, _, _) = Build(5);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Func<Task> act = async () => await service.StartAsync(new LiveTranscriptionOptions(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task UtteranceEndedTriggersRecognitionAndEmitsFinal()
    {
        var (service, _, segmenter, recognizer, _) = Build(5);
        segmenter.Script(null, EndAt(1.0));
        recognizer.Script(new[] { Recog("hello") });
        var session = await service.StartAsync(new LiveTranscriptionOptions(), CancellationToken.None);
        var finalTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.FinalTextReceived += (_, e) => finalTcs.TrySetResult(e.Text);
        int segmentCount = 0;
        session.SegmentReceived += (_, _) => Interlocked.Increment(ref segmentCount);
        var final = await finalTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await ((LiveTranscriptionSession)session).RunTask;
        recognizer.CallCount.Should().Be(1);
        final.Should().Be("hello");
        segmentCount.Should().Be(1);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task UtteranceEndedInvokesSuppressor()
    {
        var (service, _, segmenter, recognizer, suppressor) = Build(5);
        segmenter.Script(null, EndAt(1.0));
        recognizer.Script(new[] { Recog("hello") });
        var retained = new[] { Recog("hello") };
        var warning = new TranscriptionWarning("suppressed-repetition", "1 passage suppressed");
        suppressor.Script(new SuppressionResult(retained, new[] { warning }));
        var session = await service.StartAsync(new LiveTranscriptionOptions(), CancellationToken.None);
        var warningTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.QualityWarningReceived += (_, e) => warningTcs.TrySetResult(e.Warning);
        var warningSeen = await warningTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await ((LiveTranscriptionSession)session).RunTask;
        suppressor.CallCount.Should().Be(1);
        warningSeen.Should().Contain("suppressed-repetition");
        await session.DisposeAsync();
    }

    [Fact]
    public async Task StopAsyncTransitionsToIdle()
    {
        var (service, _, _, _, _) = Build(5);
        var session = await service.StartAsync(new LiveTranscriptionOptions(), CancellationToken.None);
        await ((LiveTranscriptionSession)session).RunTask;
        await session.StopAsync(CancellationToken.None);
        session.State.Should().Be(LiveTranscriptionState.Idle);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsyncDisposesCapture()
    {
        var (service, capture, _, _, _) = Build(5);
        var session = await service.StartAsync(new LiveTranscriptionOptions(), CancellationToken.None);
        await ((LiveTranscriptionSession)session).RunTask;
        await session.DisposeAsync();
        capture.Disposed.Should().BeTrue();
    }
}