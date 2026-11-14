// filepath: src/FileHarvester.Tests/ImageTextify/Gate0Diagnostics.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    1.3.0 (Phase 5 Gate 0)
// purpose:  Gate 0 verification spike for the audio/video phase. Confirms
//           Whisper.net 1.9.1 builds and runs on net10, prints every
//           segment field so the decision record can name real fields,
//           exercises WhisperVadFactory (Silero) on a synthetic
//           silence/speech clip, prints the actual WhisperProcessorBuilder
//           method names, and reports which native runtime the loader
//           selected. Corrected to the 1.9.1 API after first-run compile
//           discovery (F-AV-004).
//
// RELATIONSHIPS
//   Implements : n/a (test file)
//   Depends on : Whisper.net 1.9.1, Whisper.net.Runtime 1.9.1,
//                Whisper.net.Runtime.NoAvx 1.9.1
//   Used by    : dotnet test --filter Gate0Diagnostics
//   See also   : docs/ImageTextify/ImageTextify-Audio-Video-Technology-Decision.md
//
// NOTES
//   - Discovery spike, not a production test. Its assertions are minimal;
//     the value is the printed findings file.
//   - Network dependency: first run downloads a Whisper GGML model AND a
//     Silero VAD GGML model through WhisperGgmlDownloader.Default.
//   - Writes exactly one file under bench/probe/gate0/.
//   - Respects the project's analyzers: CA1707, CA1305, CA1845, CA1861,
//     xUnit2020.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;
using Xunit;
using Xunit.Abstractions;

namespace FileHarvester.Tests.ImageTextify;

public sealed class Gate0Diagnostics
{
    private static readonly byte[] RiffMagic = { (byte)'R', (byte)'I', (byte)'F', (byte)'F' };
    private static readonly byte[] WaveMagic = { (byte)'W', (byte)'A', (byte)'V', (byte)'E' };
    private static readonly byte[] FmtMagic  = { (byte)'f', (byte)'m', (byte)'t', (byte)' ' };
    private static readonly byte[] DataMagic = { (byte)'d', (byte)'a', (byte)'t', (byte)'a' };

    private readonly ITestOutputHelper _out;

    public Gate0Diagnostics(ITestOutputHelper output) { _out = output; }

    [Fact]
    public async Task Gate0AllChecks()
    {
        var findings = new List<string>();
        void L(string s) { _out.WriteLine(s); findings.Add(s); }

        var wavPath = Path.Combine(Path.GetTempPath(), "gate0_synthetic_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".wav");
        WriteSyntheticWav(wavPath, seconds: 5, sampleRate: 16000, speechFrom: 1.5, speechTo: 3.5);
        L("=== SYNTHETIC WAV ===");
        L("path: " + wavPath);
        L("bytes: " + new FileInfo(wavPath).Length.ToString(CultureInfo.InvariantCulture));

        L("");
        L("=== BUILDER METHOD DISCOVERY ===");
        try
        {
            var factoryType = typeof(WhisperFactory);
            L("WhisperFactory methods: " + string.Join(", ", factoryType.GetMethods().Where(m => m.IsPublic && !m.IsSpecialName).Select(m => m.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal)));
        }
        catch (Exception ex) { L("Factory reflection FAILED: " + ex.GetType().Name + ": " + ex.Message); }
        try
        {
            var builderType = typeof(WhisperProcessorBuilder);
            L("WhisperProcessorBuilder methods: " + string.Join(", ", builderType.GetMethods().Where(m => m.IsPublic && !m.IsSpecialName).Select(m => m.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal)));
        }
        catch (Exception ex) { L("Builder reflection FAILED: " + ex.GetType().Name + ": " + ex.Message); }

        L("");
        L("=== WHISPER MODEL DOWNLOAD (small) ===");
        var whisperModelPath = Path.Combine(Path.GetTempPath(), "gate0_whisper_small.ggml");
        try
        {
            if (!File.Exists(whisperModelPath))
            {
                L("downloading whisper-small via WhisperGgmlDownloader.Default");
                using var dl = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.Small);
                using var fs = File.Create(whisperModelPath);
                await dl.CopyToAsync(fs);
            }
            L("whisper model: " + whisperModelPath);
            L("whisper model bytes: " + new FileInfo(whisperModelPath).Length.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            L("WHISPER MODEL DOWNLOAD FAILED: " + ex.GetType().Name + ": " + ex.Message);
            WriteFindings(findings);
            Assert.Fail("Gate 0 whisper model download failed. See console findings.");
        }

        L("");
        L("=== SILERO VAD MODEL DOWNLOAD ===");
        var vadModelPath = Path.Combine(Path.GetTempPath(), "gate0_silero_vad.ggml");
        try
        {
            if (!File.Exists(vadModelPath))
            {
                L("downloading Silero VAD via WhisperGgmlDownloader.Default.GetGgmlSileroVadModelAsync");
                using var dl = await WhisperGgmlDownloader.Default.GetGgmlSileroVadModelAsync();
                using var fs = File.Create(vadModelPath);
                await dl.CopyToAsync(fs);
            }
            L("vad model: " + vadModelPath);
            L("vad model bytes: " + new FileInfo(vadModelPath).Length.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            L("SILERO VAD DOWNLOAD FAILED: " + ex.GetType().Name + ": " + ex.Message);
        }

        L("");
        L("=== TRANSCRIPTION (small) + SEGMENT FIELDS ===");
        var segments = new List<SegmentData>();
        try
        {
            using var factory = WhisperFactory.FromPath(whisperModelPath);
            using var processor = factory.CreateBuilder().WithLanguageDetection().Build();
            using var fileStream = File.OpenRead(wavPath);
            await foreach (var seg in processor.ProcessAsync(fileStream, CancellationToken.None))
            {
                segments.Add(seg);
                L("segment: Start=" + seg.Start + " End=" + seg.End + " Text=" + Trunc(seg.Text));
                var t = seg.GetType();
                foreach (var p in t.GetProperties())
                {
                    object? v = null; try { v = p.GetValue(seg); } catch { }
                    L("  " + p.Name + " = " + (v?.ToString() ?? "(null)"));
                }
            }
            if (segments.Count == 0) L("(no segments returned)");
        }
        catch (Exception ex) { L("TRANSCRIPTION FAILED: " + ex.GetType().Name + ": " + ex.Message); }

        L("");
        L("=== SILERO VAD REGIONS ===");
        if (File.Exists(vadModelPath))
        {
            try
            {
                using var vadFactory = WhisperVadFactory.FromPath(vadModelPath);
                using var vad = vadFactory.CreateBuilder().Build();
                using var s = File.OpenRead(wavPath);
                var regions = await vad.DetectSpeechAsync(s, CancellationToken.None);
                L("regions: " + regions.Count.ToString(CultureInfo.InvariantCulture));
                foreach (var r in regions)
                    L("vad region: Start=" + r.Start + " End=" + r.End);
            }
            catch (Exception ex) { L("VAD FAILED: " + ex.GetType().Name + ": " + ex.Message); }
        }
        else L("VAD skipped: model not available");

        L("");
        L("=== RUNTIME SELECTION ===");
        try
        {
            var order = RuntimeOptions.RuntimeLibraryOrder;
            L("RuntimeLibraryOrder: " + (order is null ? "(null)" : string.Join(", ", order.Select(r => r.ToString()))));
        }
        catch (Exception ex) { L("RuntimeOptions inspection FAILED: " + ex.GetType().Name + ": " + ex.Message); }

        L("");
        L("=== RTF (small, 5s clip, CPU) ===");
        try
        {
            using var factory = WhisperFactory.FromPath(whisperModelPath);
            using var processor = factory.CreateBuilder().WithLanguageDetection().Build();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var fs2 = File.OpenRead(wavPath);
            var count = 0;
            await foreach (var _ in processor.ProcessAsync(fs2, CancellationToken.None)) count++;
            sw.Stop();
            L("segments: " + count.ToString(CultureInfo.InvariantCulture));
            L("elapsed_seconds: " + sw.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture));
            L("audio_seconds: 5");
            L("rtf: " + (sw.Elapsed.TotalSeconds / 5.0).ToString("F3", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) { L("RTF FAILED: " + ex.GetType().Name + ": " + ex.Message); }

        WriteFindings(findings);
        L("DONE.");
        findings.Count.Should().BeGreaterThan(10);
    }

    private static void WriteSyntheticWav(string path, int seconds, int sampleRate, double speechFrom, double speechTo)
    {
        var totalSamples = seconds * sampleRate;
        var dataBytes = totalSamples * 2;
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);
        bw.Write(RiffMagic); bw.Write(36 + dataBytes); bw.Write(WaveMagic); bw.Write(FmtMagic);
        bw.Write(16); bw.Write((short)1); bw.Write((short)1); bw.Write(sampleRate);
        bw.Write(sampleRate * 2); bw.Write((short)2); bw.Write((short)16);
        bw.Write(DataMagic); bw.Write(dataBytes);
        for (int i = 0; i < totalSamples; i++)
        {
            var t = (double)i / sampleRate;
            short sample = 0;
            if (t >= speechFrom && t < speechTo)
                sample = (short)(Math.Sin(2 * Math.PI * 440.0 * t) * 8000);
            bw.Write(sample);
        }
    }

    private static void WriteFindings(List<string> findings)
    {
        try
        {
            var dir = "C:\\Users\\HP\\Desktop\\file-ferry\\bench\\probe\\gate0";
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "whisper-findings-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt");
            File.WriteAllLines(file, findings);
        }
        catch { }
    }

    private static string Trunc(string? s)
    {
        if (s is null) return "(null)";
        if (s.Length <= 80) return s;
        return string.Concat(s.AsSpan(0, 80), "...");
    }
}