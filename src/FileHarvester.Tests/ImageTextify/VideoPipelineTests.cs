// filepath: src/FileHarvester.Tests/ImageTextify/VideoPipelineTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 6 Block 6.A/6.B
// purpose:  Confirms the existing FfprobeProcessAdapter and FfmpegDecodeAdapter
//           handle video containers (MP4) with no new production code. The
//           probe reads audio streams from a video; the decoder extracts the
//           audio track to a valid 16 kHz mono WAV.
//
// RELATIONSHIPS
//   Implements : n/a (test file)
//   Depends on : FfprobeProcessAdapter, FfmpegDecodeAdapter,
//                ConfiguredFfmpegLocator
//   Used by    : dotnet test --filter VideoPipelineTests
//   See also   : ImageTextify.Platform.Media.FfprobeProcessAdapter,
//                ImageTextify.Platform.Media.FfmpegDecodeAdapter
//
// NOTES
//   - No new production code. The adapters were built to be container-agnostic;
//     these tests confirm it for MP4 specifically.
//   - The LGPL FFmpeg build lacks libx264 (H.264 encoder). Video fixtures are
//     generated with the mpeg4 encoder (LGPL-safe) plus aac audio. The app's
//     production path only decodes video, so the fixture encoder choice does
//     not affect what is tested.
//   - Skips via early-return if the Gate 0.A binaries are absent (matches the
//     5.B.2 pattern; F-AV-035 records the false-green caveat).

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

public sealed class VideoPipelineTests
{
    private static readonly string Gate0BinDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Temp", "ffmpeg-gate0", "ffmpeg-master-latest-win64-lgpl", "bin");
    private static readonly string FfmpegPath = Path.Combine(Gate0BinDir, "ffmpeg.exe");
    private static readonly string FfprobePath = Path.Combine(Gate0BinDir, "ffprobe.exe");

    private static bool BinariesPresent() => File.Exists(FfmpegPath) && File.Exists(FfprobePath);

    private static async Task<string> GenerateMp4Async(string work)
    {
        var mp4 = Path.Combine(work, "sample.mp4");
        var psi = new ProcessStartInfo { FileName = FfmpegPath, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("-y"); psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("lavfi");
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add("testsrc=size=320x240:rate=25:duration=2");
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("lavfi");
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add("sine=frequency=440:duration=2");
        psi.ArgumentList.Add("-c:v"); psi.ArgumentList.Add("mpeg4");
        psi.ArgumentList.Add("-c:a"); psi.ArgumentList.Add("aac");
        psi.ArgumentList.Add("-shortest");
        psi.ArgumentList.Add(mp4);
        using var p = Process.Start(psi)!;
        await p.WaitForExitAsync(CancellationToken.None);
        if (p.ExitCode != 0) { throw new InvalidOperationException("ffmpeg MP4 generation failed"); }
        return mp4;
    }

    [Fact]
    public async Task VideoProbeReportsAudioStreamForMp4()
    {
        if (!BinariesPresent()) { return; } // skip: Gate 0.A binaries not present
        var work = Path.Combine(Path.GetTempPath(), "v6a-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var mp4 = await GenerateMp4Async(work);
            var locator = new ConfiguredFfmpegLocator(Gate0BinDir);
            var probe = new FfprobeProcessAdapter(locator);

            var result = await probe.ProbeAsync(mp4, CancellationToken.None);

            result.IsSupported.Should().BeTrue();
            result.AudioStreams.Should().NotBeEmpty();
            result.Container.Should().NotBeNullOrEmpty();
        }
        finally { try { Directory.Delete(work, recursive: true); } catch { } }
    }

    [Fact]
    public async Task VideoDecodeExtractsAudioFromMp4()
    {
        if (!BinariesPresent()) { return; }
        var work = Path.Combine(Path.GetTempPath(), "v6b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var mp4 = await GenerateMp4Async(work);
            var locator = new ConfiguredFfmpegLocator(Gate0BinDir);
            var probe = new FfprobeProcessAdapter(locator);
            var probeResult = await probe.ProbeAsync(mp4, CancellationToken.None);
            probeResult.AudioStreams.Should().NotBeEmpty();
            var audioStreamIndex = probeResult.AudioStreams[0].Index;
            audioStreamIndex.Should().NotBe(0, "video is stream 0; audio must be a different stream index");
            var decoder = new FfmpegDecodeAdapter(locator);
            var outDir = Path.Combine(work, "out");
            Directory.CreateDirectory(outDir);

            var wavPath = await decoder.DecodeAsync(mp4, audioStreamIndex, outDir, null, CancellationToken.None);

            File.Exists(wavPath).Should().BeTrue();
            new FileInfo(wavPath).Length.Should().BeGreaterThan(0);
        }
        finally { try { Directory.Delete(work, recursive: true); } catch { } }
    }

    [Fact]
    public async Task VideoDecodeProducesValidWav()
    {
        if (!BinariesPresent()) { return; }
        var work = Path.Combine(Path.GetTempPath(), "v6c-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var mp4 = await GenerateMp4Async(work);
            var locator = new ConfiguredFfmpegLocator(Gate0BinDir);
            var probe = new FfprobeProcessAdapter(locator);
            var probeResult = await probe.ProbeAsync(mp4, CancellationToken.None);
            probeResult.AudioStreams.Should().NotBeEmpty();
            var audioStreamIndex = probeResult.AudioStreams[0].Index;
            var decoder = new FfmpegDecodeAdapter(locator);
            var outDir = Path.Combine(work, "out");
            Directory.CreateDirectory(outDir);

            var wavPath = await decoder.DecodeAsync(mp4, audioStreamIndex, outDir, null, CancellationToken.None);

            var psi = new ProcessStartInfo { FileName = FfprobePath, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
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
}