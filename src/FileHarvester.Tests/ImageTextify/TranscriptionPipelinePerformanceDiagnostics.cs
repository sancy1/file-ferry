// filepath: src/FileHarvester.Tests/ImageTextify/TranscriptionPipelinePerformanceDiagnostics.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 5 Block 5.F
// purpose:  Performance and behavioral diagnostic for the full transcription
//           pipeline. Runs real FfmpegDecodeAdapter + WhisperNetSileroSegmenter
//           + WhisperNetRecognizer + HallucinationSuppressor against synthetic
//           60-second fixtures and records wall-clock time, real-time factor,
//           and confidence distribution to bench/probe/gate0/.
//
// RELATIONSHIPS
//   Implements : n/a (diagnostic test)
//   Depends on : TranscriptionPipeline, all five port implementations
//   Used by    : dotnet test --filter TranscriptionPipelinePerformanceDiagnostics
//   See also   : docs/ImageTextify/ImageTextify-Audio-Video-Technology-Decision.md,
//                docs/ImageTextify/ImageTextify-Audio-Video-Technology-Decision.md Section 8
//
// NOTES
//   - This is a diagnostic, not a threshold-setting calibration. The
//     confidence threshold for TranscriptSegmentMarkers.LowConfidence
//     cannot be set from synthetic audio; it requires real speech, which
//     this repository does not contain. F-AV-058 records that gap.
//   - Assertions are structural only: the pipeline returns a valid
//     result; silence produces NoSpeechDetected; a sine tone produces
//     either Completed or NoSpeechDetected (Gate 0.B observed [BEEP] on
//     a sine, which is a real transcript). Numbers are recorded, not
//     asserted against thresholds.
//   - Skips via early-return if the Gate 0.B model or Gate 0.A ffmpeg
//     binaries are absent.
//   - Writes one findings file under bench/probe/gate0/ per run.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ImageTextify.Core.Abstractions;
using ImageTextify.Platform.Media;
using ImageTextify.Platform.Speech;
using Xunit;
using Xunit.Abstractions;

namespace FileHarvester.Tests.ImageTextify;

public sealed class TranscriptionPipelinePerformanceDiagnostics
{
    private readonly ITestOutputHelper _out;

    public TranscriptionPipelinePerformanceDiagnostics(ITestOutputHelper output) { _out = output; }

    private static readonly string Gate0BinDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Temp", "ffmpeg-gate0", "ffmpeg-master-latest-win64-lgpl", "bin");
    private static readonly string FfmpegPath = Path.Combine(Gate0BinDir, "ffmpeg.exe");
    private static readonly string WhisperModelPath = Path.Combine(Path.GetTempPath(), "gate0_whisper_small.ggml");
    private static readonly string SileroModelPath = Path.Combine(Path.GetTempPath(), "gate0_silero_vad.ggml");

    private static bool PrerequisitesPresent() =>
        File.Exists(FfmpegPath) && File.Exists(WhisperModelPath) && File.Exists(SileroModelPath);

    private static (TranscriptionPipeline pipeline, string work) BuildPipeline()
    {
        var locator = new ConfiguredFfmpegLocator(Gate0BinDir);
        var probe = new FfprobeProcessAdapter(locator);
        var decoder = new FfmpegDecodeAdapter(locator);
        var segmenter = new WhisperNetSileroSegmenter(SileroModelPath);
        var recognizer = new WhisperNetRecognizer(WhisperModelPath);
        var suppressor = new HallucinationSuppressor();
        var work = Path.Combine(Path.GetTempPath(), "b5f-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        return (new TranscriptionPipeline(probe, decoder, segmenter, recognizer, suppressor), work);
    }

    private static void GenerateWav(string ffmpegPath, string outPath, string filter, int seconds)
    {
        var psi = new ProcessStartInfo { FileName = ffmpegPath, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("-y"); psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("lavfi");
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(filter);
        psi.ArgumentList.Add("-t"); psi.ArgumentList.Add(seconds.ToString(CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("-ar"); psi.ArgumentList.Add("16000");
        psi.ArgumentList.Add("-ac"); psi.ArgumentList.Add("1");
        psi.ArgumentList.Add("-c:a"); psi.ArgumentList.Add("pcm_s16le");
        psi.ArgumentList.Add(outPath);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (p.ExitCode != 0) { throw new InvalidOperationException("ffmpeg generation failed"); }
    }

    [Fact]
    public async Task PipelinePerformanceDiagnostic()
    {
        if (!PrerequisitesPresent()) { return; } // skip: prerequisites absent

        var findings = new List<string>();
        void L(string s) { _out.WriteLine(s); findings.Add(s); }

        L("=== TRANSCRIPTION PIPELINE PERFORMANCE DIAGNOSTIC ===");
        L("date: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC");
        L("whisper model: " + WhisperModelPath + " (" + new FileInfo(WhisperModelPath).Length.ToString(CultureInfo.InvariantCulture) + " B)");
        L("silero model: " + SileroModelPath + " (" + new FileInfo(SileroModelPath).Length.ToString(CultureInfo.InvariantCulture) + " B)");
        L("");

        var cases = new[]
        {
            new { Name = "silence-60s", Filter = "anullsrc=channel_layout=mono:sample_rate=16000", Seconds = 60 },
            new { Name = "sine-60s", Filter = "sine=frequency=440", Seconds = 60 },
        };

        foreach (var c in cases)
        {
            L("=== CASE: " + c.Name + " (" + c.Seconds.ToString(CultureInfo.InvariantCulture) + " s) ===");
            var (pipeline, work) = BuildPipeline();
            try
            {
                var wav = Path.Combine(work, c.Name + ".wav");
                GenerateWav(FfmpegPath, wav, c.Filter, c.Seconds);

                var sw = Stopwatch.StartNew();
                var result = await pipeline.RunAsync(
                    new TranscriptionRequest(wav),
                    null,
                    CancellationToken.None);
                sw.Stop();

                var rtf = sw.Elapsed.TotalSeconds / c.Seconds;
                L("outcome: " + result.Outcome.ToString());
                L("elapsed_seconds: " + sw.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture));
                L("audio_seconds: " + c.Seconds.ToString(CultureInfo.InvariantCulture));
                L("rtf: " + rtf.ToString("F3", CultureInfo.InvariantCulture));

                if (result.Document != null)
                {
                    L("segment_count: " + result.Document.Segments.Count.ToString(CultureInfo.InvariantCulture));
                    L("warning_count: " + result.Document.Warnings.Count.ToString(CultureInfo.InvariantCulture));
                    var probs = result.Document.Segments
                        .Where(s => s.Confidence.HasValue)
                        .Select(s => s.Confidence!.Value)
                        .ToList();
                    if (probs.Count > 0)
                    {
                        L("confidence_min: " + probs.Min().ToString("F3", CultureInfo.InvariantCulture));
                        L("confidence_max: " + probs.Max().ToString("F3", CultureInfo.InvariantCulture));
                        L("confidence_mean: " + probs.Average().ToString("F3", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        L("confidence: none reported");
                    }
                }

                // Structural assertions only.
                if (c.Name.StartsWith("silence", StringComparison.Ordinal))
                {
                    result.Outcome.Should().Be(TranscriptionOutcome.NoSpeechDetected,
                        "silence must produce NoSpeechDetected (VAD rejects non-speech)");
                }
                else
                {
                    result.Outcome.Should().BeOneOf(
                        new[] { TranscriptionOutcome.Completed, TranscriptionOutcome.NoSpeechDetected },
                        "a sine tone is non-speech; VAD may correctly reject it, or Whisper may return [BEEP]");
                }
                L("");
            }
            finally
            {
                try { Directory.Delete(work, recursive: true); } catch { }
            }
        }

        // Persist findings.
        try
        {
            var findDir = "C:\\Users\\HP\\Desktop\\file-ferry\\bench\\probe\\gate0";
            Directory.CreateDirectory(findDir);
            var findFile = Path.Combine(findDir, "phase5-findings-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt");
            File.WriteAllLines(findFile, findings);
            L("findings_file: " + findFile);
        }
        catch (Exception ex) { L("findings write failed: " + ex.Message); }

        L("DONE.");
        findings.Count.Should().BeGreaterThan(5);
    }
}