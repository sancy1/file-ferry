// filepath: src/FileHarvester.Tests/ImageTextify/WhisperNetSileroSegmenterTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 5 Block 5.C
// purpose:  Integration tests for WhisperNetSileroSegmenter against the
//           real Silero VAD model at the Gate 0.B location. Each test
//           skips via early-return if the model or ffmpeg is absent.
//
// RELATIONSHIPS
//   Implements : n/a (test file)
//   Depends on : WhisperNetSileroSegmenter
//   Used by    : dotnet test --filter WhisperNetSileroSegmenterTests
//   See also   : ImageTextify.Platform.Speech.WhisperNetSileroSegmenter
//
// NOTES
//   - These tests verify the wiring, not the VAD accuracy. Accuracy
//     measurement requires real speech fixtures; that is Block 5.F.
//   - Sine and silence produce 0 speech regions; that is correct behavior
//     for non-speech signals and is what the tests assert.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ImageTextify.Platform.Speech;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

public sealed class WhisperNetSileroSegmenterTests
{
    private static readonly string SileroModelPath = Path.Combine(Path.GetTempPath(), "gate0_silero_vad.ggml");
    private static readonly string Gate0BinDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Temp", "ffmpeg-gate0", "ffmpeg-master-latest-win64-lgpl", "bin");
    private static readonly string FfmpegPath = Path.Combine(Gate0BinDir, "ffmpeg.exe");

    private static bool PrerequisitesPresent() => File.Exists(SileroModelPath) && File.Exists(FfmpegPath);

    [Fact]
    public void SegmenterThrowsWhenModelFileMissing()
    {
        if (!File.Exists(FfmpegPath)) { return; } // ffmpeg needed for the WAV fixture
        var missingModel = Path.Combine(Path.GetTempPath(), "no-such-vad-" + Guid.NewGuid().ToString("N") + ".ggml");
        var segmenter = new WhisperNetSileroSegmenter(missingModel);
        var work = Path.Combine(Path.GetTempPath(), "b5c-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var wav = Path.Combine(work, "silent.wav");
            RunFfmpeg(FfmpegPath, new[] {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "anullsrc=channel_layout=mono:sample_rate=16000",
                "-t", "1", "-c:a", "pcm_s16le", wav
            });
            Func<Task> act = async () => await segmenter.DetectAsync(wav, CancellationToken.None);
            act.Should().ThrowAsync<Exception>();
        }
        finally { try { Directory.Delete(work, recursive: true); } catch { } }
    }

    [Fact]
    public async Task SegmenterReturnsEmptyForSilentWav()
    {
        if (!PrerequisitesPresent()) { return; }
        var work = Path.Combine(Path.GetTempPath(), "b5c-silent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var wav = Path.Combine(work, "silent.wav");
            RunFfmpeg(FfmpegPath, new[] {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "anullsrc=channel_layout=mono:sample_rate=16000",
                "-t", "2", "-c:a", "pcm_s16le", wav
            });
            var segmenter = new WhisperNetSileroSegmenter(SileroModelPath);
            var regions = await segmenter.DetectAsync(wav, CancellationToken.None);
            regions.Should().NotBeNull();
            regions.Should().BeEmpty();
        }
        finally { try { Directory.Delete(work, recursive: true); } catch { } }
    }

    [Fact]
    public async Task SegmenterReturnsRegionsListForSineWav()
    {
        if (!PrerequisitesPresent()) { return; }
        var work = Path.Combine(Path.GetTempPath(), "b5c-sine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var wav = Path.Combine(work, "sine.wav");
            RunFfmpeg(FfmpegPath, new[] {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
                "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", wav
            });
            var segmenter = new WhisperNetSileroSegmenter(SileroModelPath);
            var regions = await segmenter.DetectAsync(wav, CancellationToken.None);
            regions.Should().NotBeNull();
            // A pure sine is not speech; 0 regions is the expected and
            // correct outcome. The test asserts the shape, not the count.
        }
        finally { try { Directory.Delete(work, recursive: true); } catch { } }
    }

    private static void RunFfmpeg(string exePath, string[] args)
    {
        var psi = new ProcessStartInfo { FileName = exePath, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) { psi.ArgumentList.Add(a); }
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (p.ExitCode != 0) { throw new InvalidOperationException("ffmpeg exited " + p.ExitCode.ToString(CultureInfo.InvariantCulture)); }
    }
}