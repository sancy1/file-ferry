// filepath: src/FileHarvester.Tests/ImageTextify/VadNoResetSemanticsDiagnostic.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 10 Block 10.B.2
// purpose:  Measures DetectSpeechNoResetAsync return semantics across
//           successive calls on one WhisperVadProcessor. Answers whether
//           NoReset returns the growing accumulated segment list or only
//           new segments since the previous call (ITX-10.B-017).
// RELATIONSHIPS
//   Depends on : Whisper.net (WhisperVadFactory, WhisperVadProcessor, VadSegmentData)
//   See also   : SileroStreamingApiDiagnostic
// NOTES
//   - Requires Silero VAD at %LOCALAPPDATA%\FileFerry\ImageTextify\Models\silero-vad-ggml\model.bin.
//     Fails loudly if absent (F-AV-035 avoidance).
//   - Synthetic sine is not speech; zero segments is an acceptable outcome.
//     The measurement is about return-list semantics, not VAD accuracy.
//   - The parameterless ctor overload of DetectSpeechNoResetAsync uses
//     ReadOnlyMemory<float>; the [] param form is not needed here.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Xunit;
using Xunit.Abstractions;

namespace FileHarvester.Tests.ImageTextify;

public sealed class VadNoResetSemanticsDiagnostic
{
    private readonly ITestOutputHelper _out;
    public VadNoResetSemanticsDiagnostic(ITestOutputHelper output) { _out = output; }

    [Fact]
    public async Task DumpNoResetReturnSemantics()
    {
        var modelPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FileFerry", "ImageTextify", "Models", "silero-vad-ggml", "model.bin");
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("Silero VAD not found: " + modelPath, modelPath);

        using var factory = WhisperVadFactory.FromPath(modelPath);
        using var vad = factory.CreateBuilder().Build();

        const int chunk = 1600;
        int cumulative = 0;

        _out.WriteLine("=== SILENCE ===");
        for (int i = 0; i < 2; i++)
        {
            var s = new float[chunk];
            cumulative += chunk;
            var r = await vad.DetectSpeechNoResetAsync(new ReadOnlyMemory<float>(s), CancellationToken.None);
            _out.WriteLine("call " + (i+1) + " silence  cum=" + cumulative + "  count=" + r.Count + "  " + Format(r));
        }

        _out.WriteLine("=== SINE 220Hz@0.3 ===");
        for (int i = 0; i < 4; i++)
        {
            var s = new float[chunk];
            for (int j = 0; j < chunk; j++)
                s[j] = (float)(0.3 * Math.Sin(2.0 * Math.PI * 220.0 * (cumulative + j) / 16000.0));
            cumulative += chunk;
            var r = await vad.DetectSpeechNoResetAsync(new ReadOnlyMemory<float>(s), CancellationToken.None);
            _out.WriteLine("call " + (i+1) + " sine     cum=" + cumulative + "  count=" + r.Count + "  " + Format(r));
        }

        vad.ResetState();
        _out.WriteLine("=== POST-RESET SILENCE ===");
        for (int i = 0; i < 1; i++)
        {
            var s = new float[chunk];
            cumulative += chunk;
            var r = await vad.DetectSpeechNoResetAsync(new ReadOnlyMemory<float>(s), CancellationToken.None);
            _out.WriteLine("call " + (i+1) + " reset    cum=" + cumulative + "  count=" + r.Count + "  " + Format(r));
        }
    }

    private static string Format(System.Collections.Generic.IReadOnlyList<VadSegmentData> r)
    {
        if (r.Count == 0) return "(empty)";
        return string.Join("; ", r.Select(s => "[" + s.Start.TotalSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + ".." + s.End.TotalSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + "]"));
    }
}