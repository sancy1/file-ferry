// filepath: src/FileHarvester.Tests/ImageTextify/LiveTranscriptionFakes.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 10 Block 10.D.4
// purpose:  Test fakes for ILiveTranscriptionService and
//           ILiveTranscriptionSession. Used by
//           LiveTranscriptionViewModelTests to drive the ViewModel's
//           state machine without a real service or a real microphone.
// RELATIONSHIPS
//   Implements : ILiveTranscriptionService, ILiveTranscriptionSession
//   Used by    : LiveTranscriptionViewModelTests
//   See also   : ImageTextify.Platform.Audio.LiveTranscriptionService
// NOTES
//   - The session fake is scriptable: it exposes RaiseFinal and
//     RaiseWarning so the test can drive event delivery at will.
//   - State is settable on the fake (the interface declares it
//     get-only; the fake's property is a superset).
//   - The service fake is a script queue of object: either a
//     FakeLiveTranscriptionSession to return, or an Exception to throw.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;

namespace FileHarvester.Tests.ImageTextify;

internal sealed class FakeLiveTranscriptionSession : ILiveTranscriptionSession
{
    public event EventHandler<InterimTextReceivedEventArgs>? InterimTextReceived;
    public event EventHandler<FinalTextReceivedEventArgs>? FinalTextReceived;
    public event EventHandler<SegmentReceivedEventArgs>? SegmentReceived;
    public event EventHandler<QualityWarningReceivedEventArgs>? QualityWarningReceived;
    public event EventHandler<TranscriptionActivityEventArgs>? TranscriptionActivityChanged;

    public LiveTranscriptionState State { get; set; } = LiveTranscriptionState.Listening;
    public bool Stopped { get; private set; }
    public bool Disposed { get; private set; }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stopped = true;
        State = LiveTranscriptionState.Idle;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    public void RaiseFinal(string text) =>
        FinalTextReceived?.Invoke(this, new FinalTextReceivedEventArgs(text));

    public void RaiseWarning(string warning) =>
        QualityWarningReceived?.Invoke(this, new QualityWarningReceivedEventArgs(warning));

    public void RaiseSegment(TranscriptSegment segment) =>
        SegmentReceived?.Invoke(this, new SegmentReceivedEventArgs(segment));

    public void RaiseInterim(string text) =>
        InterimTextReceived?.Invoke(this, new InterimTextReceivedEventArgs(text));

    public void RaiseActivity(bool isTranscribing) =>
        TranscriptionActivityChanged?.Invoke(this, new TranscriptionActivityEventArgs(isTranscribing));
}

internal sealed class FakeLiveTranscriptionService : ILiveTranscriptionService
{
    private readonly Queue<object> _script = new();

    public int CallCount { get; private set; }
    public LiveTranscriptionOptions? LastOptions { get; private set; }

    public FakeLiveTranscriptionService ScriptSession(FakeLiveTranscriptionSession session)
    {
        _script.Enqueue(session);
        return this;
    }

    public FakeLiveTranscriptionService ScriptException(Exception ex)
    {
        _script.Enqueue(ex);
        return this;
    }

    public Task<ILiveTranscriptionSession> StartAsync(LiveTranscriptionOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        LastOptions = options;
        object item = _script.Count > 0 ? _script.Dequeue() : new FakeLiveTranscriptionSession();
        if (item is Exception ex) { throw ex; }
        return Task.FromResult((ILiveTranscriptionSession)(FakeLiveTranscriptionSession)item);
    }
}