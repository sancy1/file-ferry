// ============================================================================
// filepath: src/FileHarvester.Tests/ImageTextify/TextToSpeechTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 13 (Text-to-Speech)
// purpose:  Fake-based tests for TextToSpeechViewModel. The real TTS engine
//           needs a physical audio device; the VM state machine and command
//           wiring are what these tests cover. The engine is proven by the
//           Phase 13 spike and by the live page.
// RELATIONSHIPS
//   Implements : n/a (test class)
//   Depends on : TextToSpeechViewModel, ITextToSpeechService
//   Used by    : test runner
//   See also   : TextToSpeechViewModel.cs, ITextToSpeechService.cs,
//                CameraCaptureTests.cs (naming and header style)
// NOTES
//   - The fake ITextToSpeechService records each call and returns a fixed
//     path for SynthesizeToFileAsync. Nine tests cover the two input paths
//     (paste, load file) and the seven commands.
//   - Every test disposes the VM in finally (the VM implements IDisposable).
//   - No physical audio, no clock, no network. Deterministic.
// ============================================================================

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.App.ViewModels;
using ImageTextify.Core.Abstractions;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Fake-based acceptance tests for the text-to-speech view model.
/// </summary>
public sealed class TextToSpeechTests
{
    [Fact]
    public async Task SpeakCallsService()
    {
        // ARRANGE
        var fake = new FakeTtsService();
        using var vm = new TextToSpeechViewModel(fake);
        vm.TextInput = "hello";

        // ACT
        await vm.SpeakAsync();

        // ASSERT
        Assert.Equal(1, fake.SpeakCallCount);
        Assert.Equal("hello", fake.LastSpokenText);
        Assert.False(vm.IsSpeaking);
        Assert.Equal("Finished speaking.", vm.StatusText);
    }

    [Fact]
    public async Task SpeakWithEmptyTextIsNoOp()
    {
        var fake = new FakeTtsService();
        using var vm = new TextToSpeechViewModel(fake);
        vm.TextInput = "   ";

        await vm.SpeakAsync();

        Assert.Equal(0, fake.SpeakCallCount);
        Assert.Equal("Enter some text first.", vm.StatusText);
    }

    [Fact]
    public void PauseDelegatesToService()
    {
        var fake = new FakeTtsService();
        using var vm = new TextToSpeechViewModel(fake);

        vm.PauseCommand.Execute(null);

        Assert.Equal(1, fake.PauseCallCount);
    }

    [Fact]
    public void ResumeDelegatesToService()
    {
        var fake = new FakeTtsService();
        using var vm = new TextToSpeechViewModel(fake);

        vm.ResumeCommand.Execute(null);

        Assert.Equal(1, fake.ResumeCallCount);
    }

    [Fact]
    public void StopDelegatesAndClearsSpeaking()
    {
        var fake = new FakeTtsService();
        using var vm = new TextToSpeechViewModel(fake);

        vm.StopCommand.Execute(null);

        Assert.Equal(1, fake.StopCallCount);
        Assert.False(vm.IsSpeaking);
    }

    [Fact]
    public async Task SynthesizeWithEmptyTextIsNoOp()
    {
        var fake = new FakeTtsService();
        var pickerCalled = false;
        using var vm = new TextToSpeechViewModel(fake,
            onPickTextFile: null,
            onPickSavePath: (_, __) => { pickerCalled = true; return Task.FromResult<string?>(null); });
        vm.TextInput = "";

        await vm.SynthesizeAsync();

        Assert.False(pickerCalled);
        Assert.Equal(0, fake.SynthesizeCallCount);
        Assert.Equal("Enter some text first.", vm.StatusText);
    }

    [Fact]
    public async Task SynthesizeCallsServiceWithPickedPath()
    {
        var fake = new FakeTtsService();
        var picked = @"C:\out.wav";
        using var vm = new TextToSpeechViewModel(fake,
            onPickTextFile: null,
            onPickSavePath: (_, __) => Task.FromResult<string?>(picked));
        vm.TextInput = "hello";

        await vm.SynthesizeAsync();

        Assert.Equal(1, fake.SynthesizeCallCount);
        Assert.Equal("hello", fake.LastSynthesizedText);
        Assert.Equal(picked, fake.LastSynthesizedPath);
        Assert.Equal(picked, vm.LastSavedPath);
        Assert.True(vm.HasResult);
    }

    [Fact]
    public void ResetClearsState()
    {
        var fake = new FakeTtsService();
        using var vm = new TextToSpeechViewModel(fake);
        vm.TextInput = "hello";

        vm.ResetCommand.Execute(null);

        Assert.Equal(string.Empty, vm.TextInput);
        Assert.Null(vm.LastSavedPath);
        Assert.False(vm.HasResult);
    }

    [Fact]
    public async Task LoadFileFillsTextInputFromPicker()
    {
        var fake = new FakeTtsService();
        var temp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(temp, "from file");
            using var vm = new TextToSpeechViewModel(fake,
                onPickTextFile: _ => Task.FromResult<string?>(temp),
                onPickSavePath: null);

            await vm.LoadFileAsync();

            Assert.Equal("from file", vm.TextInput);
            Assert.True(vm.HasText);
        }
        finally { try { File.Delete(temp); } catch { } }
    }

    /// <summary>
    /// Fake ITextToSpeechService. Records each call; returns a fixed success
    /// for synthesis. Does not speak, does not write files.
    /// </summary>
    private sealed class FakeTtsService : ITextToSpeechService
    {
        public int SpeakCallCount { get; private set; }
        public int PauseCallCount { get; private set; }
        public int ResumeCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public int SynthesizeCallCount { get; private set; }
        public string? LastSpokenText { get; private set; }
        public string? LastSynthesizedText { get; private set; }
        public string? LastSynthesizedPath { get; private set; }

        public bool IsSpeaking => false;

        public Task SpeakAsync(string text, CancellationToken cancellationToken)
        {
            SpeakCallCount++;
            LastSpokenText = text;
            return Task.CompletedTask;
        }

        public void Pause() => PauseCallCount++;
        public void ResumeSpeaking() => ResumeCallCount++;
        public void StopSpeaking() => StopCallCount++;

        public Task<string> SynthesizeToFileAsync(string text, string outputPath, CancellationToken cancellationToken)
        {
            SynthesizeCallCount++;
            LastSynthesizedText = text;
            LastSynthesizedPath = outputPath;
            return Task.FromResult(outputPath);
        }
    }
}