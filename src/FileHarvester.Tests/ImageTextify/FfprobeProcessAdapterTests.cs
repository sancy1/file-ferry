// filepath: src/FileHarvester.Tests/ImageTextify/FfprobeProcessAdapterTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 5 Block 5.B.2
// purpose:  Integration tests for FfprobeProcessAdapter against the real
//           pinned ffprobe binary. Each test skips if the binary is not
//           present, so the suite stays green on machines without it.
//
// RELATIONSHIPS
//   Implements : n/a (test file)
//   Depends on : FfprobeProcessAdapter, ConfiguredFfmpegLocator
//   Used by    : dotnet test --filter FfprobeProcessAdapterTests
//   See also   : ImageTextify.Platform.Media.FfprobeProcessAdapter
//
// NOTES
//   - Tests use the Gate 0.A pinned binary at
//     %TEMP%\ffmpeg-gate0\ffmpeg-master-latest-win64-lgpl\bin\.
//   - If that binary is not present, each test calls Assert.Skip.
//   - Fixtures are generated with the pinned ffmpeg binary in a temp dir
//     and deleted on exit.

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

public sealed class FfprobeProcessAdapterTests
{
    private static readonly string Gate0BinDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Temp", "ffmpeg-gate0", "ffmpeg-master-latest-win64-lgpl", "bin");

    private static bool BinariesPresent() =>
        File.Exists(Path.Combine(Gate0BinDir, "ffmpeg.exe")) &&
        File.Exists(Path.Combine(Gate0BinDir, "ffprobe.exe"));

    [Fact]
    public async Task FfprobeProbeReportsAudioStreamsForMp3()
    {
        if (!BinariesPresent()) { return; } // skip: ffmpeg binary not present at Gate 0.A location
        var (ffmpeg, _) = (Path.Combine(Gate0BinDir, "ffmpeg.exe"), Path.Combine(Gate0BinDir, "ffprobe.exe"));
        var work = Path.Combine(Path.GetTempPath(), "b5b2-mp3-" + Guid.NewGuid().ToString("N"));
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
            var adapter = new FfprobeProcessAdapter(locator);
            var result = await adapter.ProbeAsync(mp3, CancellationToken.None);

            result.IsSupported.Should().BeTrue();
            result.AudioStreams.Should().NotBeEmpty();
            result.AudioStreams[0].Codec.Should().NotBeNullOrEmpty();
        }
        finally { try { Directory.Delete(work, recursive: true); } catch { } }
    }

    [Fact]
    public async Task FfprobeProbeReportsDuration()
    {
        if (!BinariesPresent()) { return; } // skip: ffmpeg binary not present at Gate 0.A location
        var ffmpeg = Path.Combine(Gate0BinDir, "ffmpeg.exe");
        var work = Path.Combine(Path.GetTempPath(), "b5b2-dur-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var wav = Path.Combine(work, "sample.wav");
            await RunProcessAsync(ffmpeg, new[] {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=3",
                "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", wav
            }, CancellationToken.None);

            var locator = new ConfiguredFfmpegLocator(Gate0BinDir);
            var adapter = new FfprobeProcessAdapter(locator);
            var result = await adapter.ProbeAsync(wav, CancellationToken.None);

            result.IsSupported.Should().BeTrue();
            result.Duration.TotalSeconds.Should().BeApproximately(3.0, 0.2);
        }
        finally { try { Directory.Delete(work, recursive: true); } catch { } }
    }

    [Fact]
    public async Task FfprobeProbeReportsUnsupportedForBogusExtension()
    {
        if (!BinariesPresent()) { return; } // skip: ffmpeg binary not present at Gate 0.A location
        var work = Path.Combine(Path.GetTempPath(), "b5b2-bogus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var bogus = Path.Combine(work, "sample.xyz");
            File.WriteAllText(bogus, "this is not media");

            var locator = new ConfiguredFfmpegLocator(Gate0BinDir);
            var adapter = new FfprobeProcessAdapter(locator);
            var result = await adapter.ProbeAsync(bogus, CancellationToken.None);

            result.IsSupported.Should().BeFalse();
            result.UnsupportedReason.Should().NotBeNullOrEmpty();
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