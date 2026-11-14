// filepath: src/FileHarvester.Tests/ImageTextify/LiveTranscriptionViewModelTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 10 Block 10.D.4
// purpose:  Behaviour tests for LiveTranscriptionViewModel: state machine
//           transitions, permission denial, transcript row append, clear,
//           pause and resume.
// RELATIONSHIPS
//   Implements : n/a (test class)
//   Depends on : LiveTranscriptionViewModel, FakeLiveTranscriptionService,
//                FakeLiveTranscriptionSession, InlineUiDispatcher,
//                AsyncRelayCommand<T>
//   Used by    : dotnet test --filter LiveTranscriptionViewModelTests
//   See also   : FileHarvester.App.ViewModels.LiveTranscriptionViewModel
// NOTES
//   - The tests await AsyncRelayCommand<T>.ExecutionTask (verified in
//     AsyncRelayCommand.cs, line 140). No Task.Delay, no polling.
//   - InlineUiDispatcher runs dispatched actions synchronously, so event
//     handlers that add transcript rows complete before the raise returns.

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ImageTextify.Core.Abstractions;
using FileHarvester.App.ViewModels;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

public sealed class LiveTranscriptionViewModelTests
{
    private static (LiveTranscriptionViewModel Vm, FakeLiveTranscriptionService Svc, FakeLiveTranscriptionSession Session) Build()
    {
        var svc = new FakeLiveTranscriptionService();
        var session = new FakeLiveTranscriptionSession();
        svc.ScriptSession(session);
        var vm = new LiveTranscriptionViewModel(svc, new InlineUiDispatcher());
        return (vm, svc, session);
    }

    private static async Task StartAsync(LiveTranscriptionViewModel vm)
    {
        var cmd = (AsyncRelayCommand<object>)vm.StartCommand;
        cmd.Execute(null);
        await cmd.ExecutionTask;
    }

    [Fact]
    public async Task StartTransitionsToListening()
    {
        var (vm, svc, _) = Build();
        await StartAsync(vm);
        vm.State.Should().Be(LiveTranscriptionState.Listening);
        vm.IsListening.Should().BeTrue();
        svc.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task StopTransitionsToIdle()
    {
        var (vm, _, session) = Build();
        await StartAsync(vm);
        var cmd = (AsyncRelayCommand<object>)vm.StopCommand;
        cmd.Execute(null);
        await cmd.ExecutionTask;
        vm.State.Should().Be(LiveTranscriptionState.Idle);
        vm.IsIdle.Should().BeTrue();
        session.Stopped.Should().BeTrue();
    }

    [Fact]
    public async Task StartWithAudioUnavailableSetsPermissionDenied()
    {
        var svc = new FakeLiveTranscriptionService();
        svc.ScriptException(new AudioUnavailableException("Microphone denied"));
        var vm = new LiveTranscriptionViewModel(svc, new InlineUiDispatcher());
        await StartAsync(vm);
        vm.State.Should().Be(LiveTranscriptionState.Failed);
        vm.ShowPermissionDenied.Should().BeTrue();
        vm.PermissionDeniedText.Should().Contain("denied");
    }

    [Fact]
    public async Task FinalTextReceivedAddsTranscriptRow()
    {
        var (vm, _, session) = Build();
        await StartAsync(vm);
        session.RaiseFinal("hello");
        vm.Transcript.Should().ContainSingle();
        vm.Transcript[0].Text.Should().Be("hello");
        vm.Transcript[0].IsInterim.Should().BeFalse();
    }

    [Fact]
    public async Task ClearEmptiesTranscript()
    {
        var (vm, _, session) = Build();
        await StartAsync(vm);
        session.RaiseFinal("hello");
        vm.Transcript.Should().ContainSingle();
        vm.ClearCommand.Execute(null);
        vm.Transcript.Should().BeEmpty();
    }

    [Fact]
    public async Task PauseThenResumeTogglesState()
    {
        var (vm, _, _) = Build();
        await StartAsync(vm);
        vm.PauseCommand.Execute(null);
        vm.State.Should().Be(LiveTranscriptionState.Paused);
        vm.IsPaused.Should().BeTrue();
        vm.ResumeCommand.Execute(null);
        vm.State.Should().Be(LiveTranscriptionState.Listening);
        vm.IsListening.Should().BeTrue();
    }
}