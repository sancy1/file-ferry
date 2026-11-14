// filepath: src/FileHarvester.Tests/ImageTextify/ModelFakes.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 10 Block 10.C.4
// purpose:  Test fakes for IStreamingSpeechSegmenter, ISpeechRecognizer,
//           and IHallucinationSuppressor. Used by
//           LiveTranscriptionServiceTests to drive the live orchestrator
//           without a real VAD, recognizer, or suppressor.
// RELATIONSHIPS
//   Implements : IStreamingSpeechSegmenter, ISpeechRecognizer,
//                IHallucinationSuppressor
//   Used by    : FileHarvester.Tests.ImageTextify.LiveTranscriptionServiceTests
//   See also   : ImageTextify.Platform.Audio.LiveTranscriptionSession
// NOTES
//   - FakeStreamingSpeechSegmenter fires a scripted sequence of
//     UtteranceStarted and UtteranceEnded events synchronously inside
//     RunAsync, then returns. The session's event handlers run on the
//     calling thread, so by the time RunAsync's task completes, all
//     emissions are complete.
//   - FakeSpeechRecognizer returns scripted RecognizedSegment lists in
//     order. Every call records the WAV path so tests can assert the
//     temp file lifecycle.
//   - FakeHallucinationSuppressor returns scripted SuppressionResult
//     instances in order. Every call records the segments it received.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;

namespace FileHarvester.Tests.ImageTextify;

internal sealed class FakeStreamingSpeechSegmenter : IStreamingSpeechSegmenter
{
    private readonly Queue<(UtteranceStartedEventArgs? Start, UtteranceEndedEventArgs? End)> _script = new();

    public bool Disposed { get; private set; }

    public FakeStreamingSpeechSegmenter Script(UtteranceStartedEventArgs? start, UtteranceEndedEventArgs? end)
    {
        _script.Enqueue((start, end));
        return this;
    }

    public event EventHandler<UtteranceStartedEventArgs>? UtteranceStarted;
    public event EventHandler<UtteranceEndedEventArgs>? UtteranceEnded;

    public async Task RunAsync(Stream audioStream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audioStream);
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        while (_script.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = _script.Dequeue();
            if (item.Start is not null) { UtteranceStarted?.Invoke(this, item.Start); }
            if (item.End is not null) { UtteranceEnded?.Invoke(this, item.End); }
        }
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeSpeechRecognizer : ISpeechRecognizer
{
    private readonly Queue<IReadOnlyList<RecognizedSegment>> _script = new();

    public int CallCount { get; private set; }
    public string? LastWavPath { get; private set; }

    public FakeSpeechRecognizer Script(IReadOnlyList<RecognizedSegment> result)
    {
        _script.Enqueue(result);
        return this;
    }

    public Task<IReadOnlyList<RecognizedSegment>> TranscribeAsync(
        string wavFilePath,
        SpeechRecognizerOptions options,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wavFilePath);
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        LastWavPath = wavFilePath;
        IReadOnlyList<RecognizedSegment> result = _script.Count > 0
            ? _script.Dequeue()
            : Array.Empty<RecognizedSegment>();
        _ = options;
        _ = progress;
        return Task.FromResult(result);
    }
}

internal sealed class FakeHallucinationSuppressor : IHallucinationSuppressor
{
    private readonly Queue<SuppressionResult> _script = new();

    public int CallCount { get; private set; }
    public IReadOnlyList<RecognizedSegment>? LastSegments { get; private set; }

    public FakeHallucinationSuppressor Script(SuppressionResult result)
    {
        _script.Enqueue(result);
        return this;
    }

    public Task<SuppressionResult> SuppressAsync(
        IReadOnlyList<RecognizedSegment> segments,
        IReadOnlyList<SpeechRegion> speechRegions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        LastSegments = segments;
        _ = speechRegions;
        SuppressionResult result = _script.Count > 0
            ? _script.Dequeue()
            : new SuppressionResult(segments, Array.Empty<TranscriptionWarning>());
        return Task.FromResult(result);
    }
}