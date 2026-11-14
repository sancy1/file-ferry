// filepath: src/FileHarvester.Tests/ImageTextify/FfmpegDecodeAdapterTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 5 Block 5.B.3
// purpose:  Integration tests for FfmpegDecodeAdapter against the real
//           pinned ffmpeg binary. Each test skips via early-return if the
//           binary is not present.
//
// RELATIONSHIPS
//   Implements : n/a (test file)
//   Depends on : FfmpegDecodeAdapter, ConfiguredFfmpegLocator
//   Used by    : dotnet test --filter FfmpegDecodeAdapterTests
//   See also   : ImageTextify.Platform.Media.FfmpegDecodeAdapter

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ImageTextify.Platform.Media;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

public sealed class FfmpegDecodeAdapterTests
{
    private static readonly string Gate0BinDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Temp", "ffmpeg-gate0", "ffmpeg-master-latest-win64-lgpl", "bin");

    private static bool BinariesPresent() =>
        File.Exists(Path.Combine(Gate0BinDir, "ffmpeg.exe")) &&
        File.Exists(Path.Combine(Gate0BinDir, "ffprobe.exe"));

    [Fact]
    public async Task FfmpegDecodeAdapterProducesWavFromMp3()
    {
        if (!BinariesPresent()) { return; } // skip: ffmpeg binary not present at Gate 0.A location
        var ffmpeg = Path.Combine(Gate0BinDir, "ffmpeg.exe");
        var work = Path.Combine(Path.GetTempPath(), "b5b3-wav-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var mp3 = Path.Combine(work, "sample.mp3");
            await RunProcessAsync(ffmpeg, new[] {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
                "-ar", "44100", "-ac", "2", "-c:a", "libmp3lame", "-q:a", "4", mp3
            }, CancellationToken.None);

            var locator = new ConfiguredFfmpegLocator(Gate0BinDir);
            var adapter = new FfmpegDecodeAdapter(locator);
            var outDir = Path.Combine(work, "out");
            Directory.CreateDirectory(outDir);

            var wavPath = await adapter.DecodeAsync(mp3, 0, outDir, null, CancellationToken.None);

            File.Exists(wavPath).Should().BeTrue();
            new FileInfo(wavPath).Length.Should().BeGreaterThan(0);
        }
        finally { try { Directory.Delete(work, recursive: true); } catch { } }
    }

    [Fact]
    public async Task FfmpegDecodeAdapterProduces16KhzMonoPcm()
    {
        if (!BinariesPresent()) { return; }
        var ffmpeg = Path.Combine(Gate0BinDir, "ffmpeg.exe");
        var ffprobe = Path.Combine(Gate0BinDir, "ffprobe.exe");
        var work = Path.Combine(Path.GetTempPath(), "b5b3-16k-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var mp3 = Path.Combine(work, "sample.mp3");
            await RunProcessAsync(ffmpeg, new[] {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
                "-ar", "44100", "-ac", "2", "-c:a", "libmp3lame", "-q:a", "4", mp3
            }, CancellationToken.None);

            var locator = new ConfiguredFfmpegLocator(Gate0BinDir);
            var adapter = new FfmpegDecodeAdapter(locator);
            var outDir = Path.Combine(work, "out");
            Directory.CreateDirectory(outDir);

            var wavPath = await adapter.DecodeAsync(mp3, 0, outDir, null, CancellationToken.None);

            var psi = new ProcessStartInfo { FileName = ffprobe, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("quiet");
            psi.ArgumentList.Add("-print_format"); psi.ArgumentList.Add("json");
            psi.ArgumentList.Add("-show_streams");
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(wavPath);
            using var p = Process.Start(psi)!;
            var json = await p.StandardOutput.ReadToEndAsync(CancellationToken.None);
            await p.WaitForExitAsync(CancellationToken.None);

            json.Should().Contain("\"sample_rate\": \"16000\"");
            json.Should().Contain("\"channels\": 1");
        }
        finally { try { Directory.Delete(work, recursive: true); } catch { } }
    }

    [Fact]
    public async Task FfmpegDecodeAdapterDeletesPartialOutputOnCancel()
    {
        if (!BinariesPresent()) { return; }
        var ffmpeg = Path.Combine(Gate0BinDir, "ffmpeg.exe");
        var work = Path.Combine(Path.GetTempPath(), "b5b3-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            // Generate a 30-second MP3 to give the decoder real work to do.
            var mp3 = Path.Combine(work, "sample.mp3");
            await RunProcessAsync(ffmpeg, new[] {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=30",
                "-ar", "44100", "-ac", "2", "-c:a", "libmp3lame", "-q:a", "4", mp3
            }, CancellationToken.None);

            var locator = new ConfiguredFfmpegLocator(Gate0BinDir);
            var adapter = new FfmpegDecodeAdapter(locator);
            var outDir = Path.Combine(work, "out");
            Directory.CreateDirectory(outDir);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            Func<Task> act = async () => await adapter.DecodeAsync(mp3, 0, outDir, null, cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();

            // The adapter must have removed any partial output.
            Directory.GetFiles(outDir).Should().BeEmpty();
        }
        finally { try { Directory.Delete(work, recursive: true); } catch { } }
    }

    private static async Task RunProcessAsync(string exePath, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo { FileName = exePath, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) { psi.ArgumentList.Add(a); }
        using var p = Process.Start(psi)!;
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0) { throw new InvalidOperationException("process " + exePath + " exited " + p.ExitCode.ToString(CultureInfo.InvariantCulture)); }
    }
}