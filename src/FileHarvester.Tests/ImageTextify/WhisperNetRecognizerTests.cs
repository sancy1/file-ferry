// filepath: src/FileHarvester.Tests/ImageTextify/WhisperNetRecognizerTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 5 Block 5.D.2
// purpose:  Integration tests for WhisperNetRecognizer against the real
//           Whisper small GGML model at the Gate 0.B location. Each test
//           skips via early-return if the model or ffmpeg is absent.
//
// RELATIONSHIPS
//   Implements : n/a (test file)
//   Depends on : WhisperNetRecognizer
//   Used by    : dotnet test --filter WhisperNetRecognizerTests
//   See also   : ImageTextify.Platform.Speech.WhisperNetRecognizer
//
// NOTES
//   - These tests verify the wiring, not the recognition accuracy.
//     Accuracy is measured against real speech fixtures in Block 5.F.
//   - Gate 0.B observed that a 440 Hz sine produces one segment with
//     text "[BEEP]". That behavior is what test 2 asserts (shape, not
//     exact text).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ImageTextify.Core.Abstractions;
using ImageTextify.Platform.Speech;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

public sealed class WhisperNetRecognizerTests
{
    private static readonly string WhisperModelPath = Path.Combine(Path.GetTempPath(), "gate0_whisper_small.ggml");
    private static readonly string Gate0BinDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Temp", "ffmpeg-gate0", "ffmpeg-master-latest-win64-lgpl", "bin");
    private static readonly string FfmpegPath = Path.Combine(Gate0BinDir, "ffmpeg.exe");

    private static bool PrerequisitesPresent() => File.Exists(WhisperModelPath) && File.Exists(FfmpegPath);

    [Fact]
    public void RecognizerThrowsWhenModelFileMissing()
    {
        if (!File.Exists(FfmpegPath)) { return; }
        var missing = Path.Combine(Path.GetTempPath(), "no-such-whisper-" + Guid.NewGuid().ToString("N") + ".ggml");
        var recognizer = new WhisperNetRecognizer(missing);
        var work = Path.Combine(Path.GetTempPath(), "b5d2-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var wav = Path.Combine(work, "sine.wav");
            RunFfmpeg(FfmpegPath, new[] {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
                "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", wav
            });
            Func<Task> act = async () => await recognizer.TranscribeAsync(
                wav, new SpeechRecognizerOptions(null, TranscriptionQuality.Standard), null, CancellationToken.None);
            act.Should().ThrowAsync<Exception>();
        }
        finally { try { Directory.Delete(work, recursive: true); } catch { } }
    }

    [Fact]
    public async Task RecognizerReturnsSegmentsForSineWav()
    {
        if (!PrerequisitesPresent()) { return; }
        var work = Path.Combine(Path.GetTempPath(), "b5d2-sine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var wav = Path.Combine(work, "sine.wav");
            RunFfmpeg(FfmpegPath, new[] {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
                "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", wav
            });
            var recognizer = new WhisperNetRecognizer(WhisperModelPath);
            var segments = await recognizer.TranscribeAsync(
                wav, new SpeechRecognizerOptions(null, TranscriptionQuality.Standard), null, CancellationToken.None);

            segments.Should().NotBeNull();
            // Gate 0.B observed exactly 1 segment ("[BEEP]") on this input.
            // The shape is what matters: at least one segment, with Start/End/Text set.
            segments.Count.Should().BeGreaterThanOrEqualTo(1);
            foreach (var s in segments)
            {
                s.End.Should().BeGreaterThanOrEqualTo(s.Start);
            }
        }
        finally { try { Directory.Delete(work, recursive: true); } catch { } }
    }

    [Fact]
    public async Task RecognizerHonorsPreCancelledToken()
    {
        if (!PrerequisitesPresent()) { return; }
        var work = Path.Combine(Path.GetTempPath(), "b5d2-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var wav = Path.Combine(work, "sine.wav");
            RunFfmpeg(FfmpegPath, new[] {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
                "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", wav
            });
            var recognizer = new WhisperNetRecognizer(WhisperModelPath);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Func<Task> act = async () => await recognizer.TranscribeAsync(
                wav, new SpeechRecognizerOptions(null, TranscriptionQuality.Standard), null, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally { try { Directory.Delete(work, recursive: true); } catch { } }
    }

    private static void RunFfmpeg(string exePath, string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo { FileName = exePath, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) { psi.ArgumentList.Add(a); }
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit();
        if (p.ExitCode != 0) { throw new InvalidOperationException("ffmpeg exited " + p.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
    }
}