// ============================================================================
// FILE:     src/FileHarvester.Tests/ImageTextify/ImageTextifyAudioVideoTests.cs
// PURPOSE:  Behavior tests for the audio/video branch of
//           ImageTextifyViewModel.ExtractTextCommand. Proves the branch
//           dispatches through ITranscriptionPipeline, maps each
//           TranscriptionOutcome to the correct result status, and
//           reports a clear message when the pipeline is not registered.
// RELATION: Consumes ImageTextifyViewModel, FakeTranscriptionPipeline,
//           FakeImageTextifySettingsStore, FakeImageOcrEngine,
//           FakePdfTextExtractor, and a local FakeIntake.
// NOTES:    FakeTranscriptionPipeline is a nested private class, matching
//           the FakeIntake pattern in ImageTextifyExtractCommandTests.
//           Each test builds its own row and temp file; nothing is shared.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.App.ViewModels;
using FluentAssertions;
using ImageTextify.Core.Abstractions;
using ImageTextify.Core.Domain;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

public sealed class ImageTextifyAudioVideoTests
{
    private static ImageTextifyFileRowViewModel MakeAudioRow(string fullPath)
    {
        var validation = new ImageTextifyValidationResult(
            true,
            ImageTextifyValidationReason.Valid,
            Path.GetFileName(fullPath),
            fullPath,
            1);
        return new ImageTextifyFileRowViewModel(
            validation,
            ImageTextifyIntakeReason.Accepted,
            ImageTextifyCategory.Audio,
            new RelayCommand(() => { }));
    }

    private static string MakeTempFiles(params string[] names)
    {
        var dir = Path.Combine(Path.GetTempPath(), "file-ferry-av-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(dir, name), "x");
        }
        return dir;
    }

    [Fact]
    public async Task ExtractCommandCallsPipelineForAudioRows()
    {
        var dir = MakeTempFiles("a.mp3", "b.mp3");
        try
        {
            var pipeline = new FakeTranscriptionPipeline
            {
                ResultToReturn = new TranscriptionResult(
                    TranscriptionOutcome.Completed,
                    new TranscriptDocument(
                        "a.mp3",
                        TimeSpan.FromSeconds(10),
                        "en",
                        0.99,
                        new List<TranscriptSegment>
                        {
                            new(TimeSpan.Zero, TimeSpan.FromSeconds(1), "hello", null, 0.9, TranscriptSegmentMarkers.None),
                            new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "world", null, 0.9, TranscriptSegmentMarkers.None),
                        },
                        Array.Empty<TranscriptionWarning>(),
                        new EngineProvenance("whisper-small", new string('0', 64), "whisper.net-1.9.1", "silero-vad")),
                    null),
            };
            var vm = new ImageTextifyViewModel(
                new FakeIntake(),
                new FakeImageTextifySettingsStore(),
                new FakeImageOcrEngine(),
                new FakePdfTextExtractor(),
                pipeline);
            vm.Files.Add(MakeAudioRow(Path.Combine(dir, "a.mp3")));
            vm.Files.Add(MakeAudioRow(Path.Combine(dir, "b.mp3")));

            var cmd = (AsyncRelayCommand<object>)vm.ExtractTextCommand;
            cmd.Execute(null);
            await cmd.ExecutionTask;

            pipeline.CallCount.Should().Be(2);
            vm.Results.Count.Should().Be(2);
            vm.Results.Should().OnlyContain(r => r.StatusText == "Extracted");
            vm.Results[0].ExtractedText.Should().Contain("hello");
            vm.Results[0].ExtractedText.Should().Contain("world");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ExtractCommandMapsCancelledOutcome()
    {
        var dir = MakeTempFiles("a.mp3");
        try
        {
            var pipeline = new FakeTranscriptionPipeline
            {
                ResultToReturn = new TranscriptionResult(TranscriptionOutcome.Cancelled, null, "Transcription was cancelled."),
            };
            var vm = new ImageTextifyViewModel(
                new FakeIntake(), new FakeImageTextifySettingsStore(), new FakeImageOcrEngine(), new FakePdfTextExtractor(), pipeline);
            vm.Files.Add(MakeAudioRow(Path.Combine(dir, "a.mp3")));

            var cmd = (AsyncRelayCommand<object>)vm.ExtractTextCommand;
            cmd.Execute(null);
            await cmd.ExecutionTask;

            vm.Results.Count.Should().Be(1);
            vm.Results[0].StatusText.Should().Be("Cancelled");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ExtractCommandMapsFailedOutcome()
    {
        var dir = MakeTempFiles("a.mp3");
        try
        {
            var pipeline = new FakeTranscriptionPipeline
            {
                ResultToReturn = new TranscriptionResult(TranscriptionOutcome.Failed, null, "audio exploded"),
            };
            var vm = new ImageTextifyViewModel(
                new FakeIntake(), new FakeImageTextifySettingsStore(), new FakeImageOcrEngine(), new FakePdfTextExtractor(), pipeline);
            vm.Files.Add(MakeAudioRow(Path.Combine(dir, "a.mp3")));

            var cmd = (AsyncRelayCommand<object>)vm.ExtractTextCommand;
            cmd.Execute(null);
            await cmd.ExecutionTask;

            vm.Results.Count.Should().Be(1);
            vm.Results[0].StatusText.Should().Be("Failed: audio exploded");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ExtractCommandReportsNotAvailableWhenPipelineMissing()
    {
        var dir = MakeTempFiles("a.mp3");
        try
        {
            var vm = new ImageTextifyViewModel(
                new FakeIntake(), new FakeImageTextifySettingsStore(), new FakeImageOcrEngine(), new FakePdfTextExtractor());
            vm.Files.Add(MakeAudioRow(Path.Combine(dir, "a.mp3")));

            var cmd = (AsyncRelayCommand<object>)vm.ExtractTextCommand;
            cmd.Execute(null);
            await cmd.ExecutionTask;

            vm.Results.Count.Should().Be(1);
            vm.Results[0].StatusText.Should().Be("Failed: audio and video transcription is not enabled on this build.");
        }
        finally { Directory.Delete(dir, true); }
    }

    private sealed class FakeIntake : IImageTextifyIntakeService
    {
        public Task<ImageTextifyIntakeDecision> EvaluateAsync(
            string fullPath,
            ImageTextifyCategory category,
            IReadOnlyCollection<string> existingFullPaths,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("The audio/video tests do not call the intake service.");
        }
    }

    private sealed class FakeTranscriptionPipeline : ITranscriptionPipeline
    {
        public int CallCount { get; private set; }
        public TranscriptionResult? ResultToReturn { get; set; }

        public Task<TranscriptionResult> RunAsync(
            TranscriptionRequest request,
            IProgress<TranscriptionProgress>? progress,
            CancellationToken cancellationToken)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            var r = ResultToReturn ?? new TranscriptionResult(
                TranscriptionOutcome.Failed, null, "FakeTranscriptionPipeline had no ResultToReturn");
            return Task.FromResult(r);
        }
    }
}