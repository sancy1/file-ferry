// filepath: src/ImageTextify.Platform/Media/FfmpegDecodeAdapter.cs
// layer:    Infrastructure
// module:   ImageTextify.Platform.Media
// since:    Phase 5 Block 5.B.3
// purpose:  Implements IAudioDecoder by spawning the pinned ffmpeg binary
//           and writing 16 kHz mono 16-bit PCM to a WAV in the caller's
//           temporary directory. Never writes next to the input file.
//
// RELATIONSHIPS
//   Implements : ImageTextify.Core.Abstractions.IAudioDecoder
//   Depends on : IFfmpegLocator, TranscriptionProgress, System.Diagnostics.Process
//   Used by    : ImageTextify.Platform.Speech.TranscriptionPipeline (via DI)
//   See also   : ImageTextify.Core.Abstractions.IAudioDecoder,
//                ImageTextify.Platform.Media.ConfiguredFfmpegLocator
//
// NOTES
//   - Hardening: argv array (never a shell string); -nostdin; -protocol_whitelist
//     file,pipe; 300-second timeout; kill entire process tree on cancel;
//     stderr drained on a background task capped at 8 KB.
//   - Unlike ffprobe, ffmpeg accepts -nostdin. That asymmetry was the
//     defect in FfprobeProcessAdapter that F-AV-036 recorded.
//   - Cancellation and any error path delete the partial output file.
//   - The output WAV lives in the caller-supplied outputDirectory; the
//     caller (the pipeline) owns deletion of that directory.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImageTextify.Core.Abstractions;

namespace ImageTextify.Platform.Media;

/// <summary>
/// Decodes an audio stream to 16 kHz mono 16-bit PCM WAV via ffmpeg.
/// </summary>
public sealed class FfmpegDecodeAdapter : IAudioDecoder
{
    private const int TimeoutMs = 300_000;
    private const int KillGraceMs = 5_000;
    private const int StderrCapBytes = 8 * 1024;

    private readonly IFfmpegLocator _locator;

    /// <summary>
    /// Constructs the adapter.
    /// </summary>
    /// <param name="locator">The FFmpeg locator. Not null.</param>
    public FfmpegDecodeAdapter(IFfmpegLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        _locator = locator;
    }

    /// <inheritdoc/>
    public async Task<string> DecodeAsync(
        string filePath,
        int streamIndex,
        string outputDirectory,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(outputDirectory);
        if (!Directory.Exists(outputDirectory))
        {
            throw new DirectoryNotFoundException("The output directory does not exist: " + outputDirectory);
        }

        var ffmpegPath = _locator.GetFfmpegPath();
        var outputPath = Path.Combine(outputDirectory, "decoded-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".wav");

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-nostdin");
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-protocol_whitelist"); psi.ArgumentList.Add("file,pipe");
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(filePath);
        psi.ArgumentList.Add("-map"); psi.ArgumentList.Add("0:" + streamIndex.ToString(CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("-vn");
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("wav");
        psi.ArgumentList.Add("-ar"); psi.ArgumentList.Add("16000");
        psi.ArgumentList.Add("-ac"); psi.ArgumentList.Add("1");
        psi.ArgumentList.Add(outputPath);

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start()) { throw new InvalidOperationException("ffmpeg did not start."); }
        }
        catch (Exception ex)
        {
            TryDelete(outputPath);
            throw new InvalidOperationException("ffmpeg failed to start: " + ex.Message, ex);
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
            TryDelete(outputPath);
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("ffmpeg did not complete within " + (TimeoutMs / 1000).ToString(CultureInfo.InvariantCulture) + " seconds.");
        }

        var stderr = await stderrTask.ConfigureAwait(false);
        _ = await stdoutTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            TryDelete(outputPath);
            throw new InvalidOperationException("ffmpeg exited with code " + process.ExitCode.ToString(CultureInfo.InvariantCulture) + ": " + stderr);
        }
        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
        {
            TryDelete(outputPath);
            throw new InvalidOperationException("ffmpeg produced no output file.");
        }

        return outputPath;
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

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) { File.Delete(path); } } catch { }
    }
}