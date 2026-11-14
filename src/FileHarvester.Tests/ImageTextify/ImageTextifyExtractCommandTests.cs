// ============================================================================
// FILE:     src/FileHarvester.Tests/ImageTextify/ImageTextifyExtractCommandTests.cs
// PURPOSE:  Behavior tests for ImageTextifyViewModel.ExtractTextCommand. Proves
//           the command iterates every accepted queue row, skips rejected rows,
//           calls IImageOcrEngine once per accepted row, populates Results,
//           and marks a failed row without stopping the remaining rows.
// RELATION: Consumes ImageTextifyViewModel, ImageTextifyFileRowViewModel,
//           ImageTextifyResultItemViewModel, FakeImageOcrEngine, and
//           FakeImageTextifySettingsStore. Uses AsyncRelayCommand<object>
//           ExecutionTask to await the fire-and-forget command.
// NOTES:    Each test writes its own temporary files under Path.GetTempPath
//           and deletes them in finally. No dependency on the eval set, the
//           ONNX models, or the Qwen GGUF. The fake engine never reads the
//           stream; that the command opened the file and called the engine is
//           the behavior under test.
// ============================================================================

using System;
using System.IO;
using System.Threading.Tasks;
using FileHarvester.App.ViewModels;
using FluentAssertions;
using ImageTextify.Core.Abstractions;
using ImageTextify.Core.Domain;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Behavior tests for <see cref="ImageTextifyViewModel.ExtractTextCommand"/>.
/// </summary>
public sealed class ImageTextifyExtractCommandTests
{
    /// <summary>
    /// Builds a queue row for the given path with the given acceptance.
    /// </summary>
    private static ImageTextifyFileRowViewModel MakeRow(string fullPath, bool accepted)
    {
        var validation = new ImageTextifyValidationResult(
            accepted,
            accepted ? ImageTextifyValidationReason.Valid : ImageTextifyValidationReason.MissingFile,
            Path.GetFileName(fullPath),
            fullPath,
            1);
        var reason = accepted ? ImageTextifyIntakeReason.Accepted : ImageTextifyIntakeReason.Rejected;
        return new ImageTextifyFileRowViewModel(validation, reason, ImageTextifyCategory.Image, new RelayCommand(() => { }));
    }

    /// <summary>
    /// Creates a fresh temp directory with the given file names and returns the directory.
    /// </summary>
    private static string MakeTempFiles(params string[] names)
    {
        var dir = Path.Combine(Path.GetTempPath(), "file-ferry-extract-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(dir, name), "x");
        }
        return dir;
    }

    /// <summary>
    /// Three accepted rows: engine called once per row, three results, count reported.
    /// </summary>
    [Fact]
    public async Task ExtractCommandProcessesAllValidRows()
    {
        var dir = MakeTempFiles("a.png", "b.png", "c.png");
        try
        {
            var engine = new FakeImageOcrEngine { ResultToReturn = new OcrResult("text", 0.9, Array.Empty<string>(), OcrTextKind.Printed, Array.Empty<OcrLineResult>()) };
            var vm = new ImageTextifyViewModel(new FakeIntake(), new FakeImageTextifySettingsStore(), engine, new FakePdfTextExtractor());
            vm.Files.Add(MakeRow(Path.Combine(dir, "a.png"), true));
            vm.Files.Add(MakeRow(Path.Combine(dir, "b.png"), true));
            vm.Files.Add(MakeRow(Path.Combine(dir, "c.png"), true));

            var cmd = (AsyncRelayCommand<object>)vm.ExtractTextCommand;
            cmd.Execute(null);
            await cmd.ExecutionTask;

            engine.CallCount.Should().Be(3);
            vm.Results.Count.Should().Be(3);
            vm.Results.Should().OnlyContain(r => r.StatusText == "Extracted");
            vm.Results.Should().OnlyContain(r => r.ExtractedText == "text");
            vm.ExtractStatusText.Should().Be("3 of 3 extracted.");
            vm.HasResults.Should().BeTrue();
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// Rejected rows are skipped; only accepted rows reach the engine.
    /// </summary>
    [Fact]
    public async Task ExtractCommandSkipsRejectedRows()
    {
        var dir = MakeTempFiles("a.png", "b.png", "c.png");
        try
        {
            var engine = new FakeImageOcrEngine();
            var vm = new ImageTextifyViewModel(new FakeIntake(), new FakeImageTextifySettingsStore(), engine, new FakePdfTextExtractor());
            vm.Files.Add(MakeRow(Path.Combine(dir, "a.png"), true));
            vm.Files.Add(MakeRow(Path.Combine(dir, "b.png"), false));
            vm.Files.Add(MakeRow(Path.Combine(dir, "c.png"), true));

            var cmd = (AsyncRelayCommand<object>)vm.ExtractTextCommand;
            cmd.Execute(null);
            await cmd.ExecutionTask;

            engine.CallCount.Should().Be(2);
            vm.Results.Count.Should().Be(2);
            vm.ExtractStatusText.Should().Be("2 of 2 extracted.");
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// A row whose engine call throws is marked Failed; the remaining rows still run.
    /// </summary>
    [Fact]
    public async Task ExtractCommandFailureMarksRowAndContinues()
    {
        var dir = MakeTempFiles("a.png", "b.png", "c.png");
        try
        {
            var engine = new FakeImageOcrEngine { ResultToReturn = new OcrResult("ok", 0.9, Array.Empty<string>(), OcrTextKind.Printed, Array.Empty<OcrLineResult>()) };
            engine.ExceptionSelector = n => n == 2 ? new InvalidOperationException("synthetic failure") : null;
            var vm = new ImageTextifyViewModel(new FakeIntake(), new FakeImageTextifySettingsStore(), engine, new FakePdfTextExtractor());
            vm.Files.Add(MakeRow(Path.Combine(dir, "a.png"), true));
            vm.Files.Add(MakeRow(Path.Combine(dir, "b.png"), true));
            vm.Files.Add(MakeRow(Path.Combine(dir, "c.png"), true));

            var cmd = (AsyncRelayCommand<object>)vm.ExtractTextCommand;
            cmd.Execute(null);
            await cmd.ExecutionTask;

            engine.CallCount.Should().Be(3);
            vm.Results.Count.Should().Be(3);
            vm.Results[0].StatusText.Should().Be("Extracted");
            vm.Results[1].StatusText.Should().Be("Failed: synthetic failure");
            vm.Results[1].ExtractedText.Should().BeEmpty();
            vm.Results[2].StatusText.Should().Be("Extracted");
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// Minimal IImageTextifyIntakeService stub; never used by the extraction tests.
    /// </summary>
    /// <summary>
    /// ResetCommand clears the queue, the results, and the extraction status.
    /// Category and Qwen setting are deliberately preserved.
    /// </summary>
    [Fact]
    public async Task ResetCommandClearsQueueResultsAndStatus()
    {
        var dir = MakeTempFiles("a.png");
        try
        {
            var engine = new FakeImageOcrEngine { ResultToReturn = new OcrResult("text", 0.9, Array.Empty<string>(), OcrTextKind.Printed, Array.Empty<OcrLineResult>()) };
            var vm = new ImageTextifyViewModel(new FakeIntake(), new FakeImageTextifySettingsStore(), engine, new FakePdfTextExtractor());
            vm.Files.Add(MakeRow(Path.Combine(dir, "a.png"), true));
            var cmd = (AsyncRelayCommand<object>)vm.ExtractTextCommand;
            cmd.Execute(null);
            await cmd.ExecutionTask;
            vm.Results.Count.Should().Be(1);
            ((RelayCommand)vm.ResetCommand).Execute(null);
            vm.Files.Should().BeEmpty();
            vm.Results.Should().BeEmpty();
            vm.ExtractStatusText.Should().Be("No extraction has run yet.");
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// BuildResultsText joins each result as a file-name header, the
    /// extracted text, and a blank line between results, with the final
    /// trailing blank line trimmed.
    /// </summary>
    [Fact]
    public void BuildResultsTextJoinsResultsWithFileHeaders()
    {
        var vm = new ImageTextifyViewModel(new FakeIntake(), new FakeImageTextifySettingsStore(), new FakeImageOcrEngine(), new FakePdfTextExtractor());
        vm.Results.Add(new ImageTextifyResultItemViewModel("file1.png", "text one", "", "Extracted"));
        vm.Results.Add(new ImageTextifyResultItemViewModel("file2.png", "text two", "", "Extracted"));
        var expected = "file1.png" + Environment.NewLine + "text one" + Environment.NewLine + Environment.NewLine + "file2.png" + Environment.NewLine + "text two";
        vm.BuildResultsText().Should().Be(expected);
    }

    private sealed class FakeIntake : IImageTextifyIntakeService
    {
        public Task<ImageTextifyIntakeDecision> EvaluateAsync(string fullPath, ImageTextifyCategory category, System.Collections.Generic.IReadOnlyCollection<string> existingFullPaths, System.Threading.CancellationToken cancellationToken)
        {
            throw new NotSupportedException("The extraction tests do not call the intake service.");
        }
    }
}
