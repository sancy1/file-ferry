// filepath: src/ImageTextify.Platform/Media/FfprobeProcessAdapter.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Media
// since:    Phase 5 Block 5.B.2
// purpose:  Implements IMediaProbe by spawning the pinned ffprobe binary
//           and parsing its JSON output. Reads the container, duration,
//           and audio streams; never decodes audio data.
//
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.IMediaProbe
//   Depends on : IFfmpegLocator, AudioProbeResult, AudioStreamInfo,
//                System.Diagnostics.Process, System.Text.Json
//   Used by    : ImageTextify.Platform.Speech.TranscriptionPipeline (via DI)
//   See also   : ImageTextify.Core.Abstractions.IMediaProbe,
//                ImageTextify.Platform.Media.ConfiguredFfmpegLocator
//
// NOTES
//   - Hardening: arguments are passed as an argv array (never a shell
    //     file,pipe prevents a crafted playlist from opening network URLs;
    //     a 30-second timeout bounds the probe; stderr is drained on a
//     a 30-second timeout bounds the probe; stderr is drained on a
//     background task and capped at 8 KB to prevent pipe deadlock;
//     cancellation kills the entire process tree.
//   - A missing ffprobe binary or a non-zero exit maps to an
//     IsSupported=false result with an UnsupportedReason. The
//     TranscriptionPipeline then maps that to TranscriptionOutcome.
//     UnsupportedMedia.
//   - Only Windows paths are assumed; the FfmpegLocator supplies the
//     binary path.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;

namespace ImageTextify.Platform.Media;

/// <summary>
/// Probes a media container by spawning ffprobe and parsing its JSON.
/// </summary>
public sealed class FfprobeProcessAdapter : IMediaProbe
{
    private const int TimeoutMs = 30_000;
    private const int KillGraceMs = 5_000;
    private const int StderrCapBytes = 8 * 1024;

    private static readonly HashSet<string> SupportedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp3", "wav", "w64", "m4a", "mov", "mp4", "aac", "flac", "ogg", "wma", "asf", "amr",
        "matroska", "webm", "avi", "flv", "wv", "3gp", "3g2", "mj2"
    };

    private static readonly HashSet<string> SupportedAudioCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "aac", "mp3", "pcm_s16le", "pcm_s24le", "pcm_s32le", "pcm_f32le", "pcm_f64le",
        "flac", "vorbis", "opus", "ac3", "eac3", "wmav2", "amr_nb", "amr_wb", "alac"
    };

    private readonly IFfmpegLocator _locator;

    /// <summary>
    /// Constructs the adapter.
    /// </summary>
    /// <param name="locator">The FFmpeg locator. Not null.</param>
    public FfprobeProcessAdapter(IFfmpegLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        _locator = locator;
    }

    /// <inheritdoc/>
    public async Task<AudioProbeResult> ProbeAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        if (!File.Exists(filePath))
        {
            return Unsupported("The file does not exist: " + filePath);
        }

        string ffprobePath;
        try { ffprobePath = _locator.GetFfprobePath(); }
        catch (FileNotFoundException ex)
        {
            return Unsupported("The pinned ffprobe binary was not found: " + ex.Message);
        }

        var psi = new ProcessStartInfo
        {
            FileName = ffprobePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("quiet");
        psi.ArgumentList.Add("-print_format"); psi.ArgumentList.Add("json");
        psi.ArgumentList.Add("-show_streams");
        psi.ArgumentList.Add("-show_format");
        psi.ArgumentList.Add("-protocol_whitelist"); psi.ArgumentList.Add("file,pipe");
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(filePath);

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start()) { return Unsupported("ffprobe did not start."); }
        }
        catch (Exception ex)
        {
            return Unsupported("ffprobe failed to start: " + ex.Message);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = DrainCappedAsync(process.StandardError, StderrCapBytes);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeoutMs);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync(new CancellationTokenSource(KillGraceMs).Token).ConfigureAwait(false); } catch { }
            cancellationToken.ThrowIfCancellationRequested();
            return Unsupported("ffprobe did not complete within " + (TimeoutMs / 1000).ToString(CultureInfo.InvariantCulture) + " seconds.");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            return Unsupported("ffprobe exited with code " + process.ExitCode.ToString(CultureInfo.InvariantCulture) + ": " + stderr);
        }

        return ParseProbeJson(stdout);
    }

    private static AudioProbeResult ParseProbeJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string container = "";
            TimeSpan duration = TimeSpan.Zero;
            if (root.TryGetProperty("format", out var format))
            {
                if (format.TryGetProperty("format_name", out var fname) && fname.ValueKind == JsonValueKind.String)
                {
                    container = fname.GetString() ?? "";
                }
                if (format.TryGetProperty("duration", out var dur) && dur.ValueKind == JsonValueKind.String)
                {
                    var durStr = dur.GetString();
                    if (durStr is not null && double.TryParse(durStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var secs))
                    {
                        duration = TimeSpan.FromSeconds(secs);
                    }
                }
            }

            var audioStreams = new List<AudioStreamInfo>();
            var anyUnsupportedCodec = false;
            if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    if (!stream.TryGetProperty("codec_type", out var codecType) || codecType.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }
                    if (!string.Equals(codecType.GetString(), "audio", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    int index = stream.TryGetProperty("index", out var idx) && idx.ValueKind == JsonValueKind.Number ? idx.GetInt32() : 0;
                    string codec = stream.TryGetProperty("codec_name", out var cn) && cn.ValueKind == JsonValueKind.String ? (cn.GetString() ?? "") : "";
                    int channels = stream.TryGetProperty("channels", out var ch) && ch.ValueKind == JsonValueKind.Number ? ch.GetInt32() : 0;
                    int sampleRate = 0;
                    if (stream.TryGetProperty("sample_rate", out var sr) && sr.ValueKind == JsonValueKind.String)
                    {
                        int.TryParse(sr.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out sampleRate);
                    }
                    audioStreams.Add(new AudioStreamInfo(index, codec, channels, sampleRate, null));
                    if (!SupportedAudioCodecs.Contains(codec)) { anyUnsupportedCodec = true; }
                }
            }

            bool anyFormatToken = false;
            foreach (var token in container.Split(','))
            {
                if (SupportedFormats.Contains(token.Trim())) { anyFormatToken = true; break; }
            }

            if (!anyFormatToken)
            {
                return new AudioProbeResult(container, duration, audioStreams, false,
                    "The container '" + container + "' is not supported.");
            }
            if (audioStreams.Count == 0)
            {
                return new AudioProbeResult(container, duration, audioStreams, true, null);
            }
            if (anyUnsupportedCodec)
            {
                return new AudioProbeResult(container, duration, audioStreams, false,
                    "One or more audio codecs are not supported.");
            }
            return new AudioProbeResult(container, duration, audioStreams, true, null);
        }
        catch (Exception ex)
        {
            return Unsupported("Could not parse ffprobe output: " + ex.Message);
        }
    }

    private static async Task<string> DrainCappedAsync(StreamReader reader, int capBytes)
    {
        var sb = new StringBuilder();
        var buffer = new char[1024];
        int total = 0;
        int read;
        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
        {
            if (total < capBytes)
            {
                int remaining = capBytes - total;
                int take = read < remaining ? read : remaining;
                sb.Append(buffer, 0, take);
                total += take;
            }
        }
        return sb.ToString();
    }

    private static AudioProbeResult Unsupported(string reason) =>
        new("", TimeSpan.Zero, Array.Empty<AudioStreamInfo>(), false, reason);
}