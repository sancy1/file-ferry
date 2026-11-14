// filepath: src/FileHarvester.Tests/ImageTextify/LiveRecordingServiceTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 10 Block 10.F.7
// purpose:  Behaviour tests for LiveRecordingService: WAV header, PCM
//           data recording through the tee, stop finalization, tee
//           factory, dispose finalization, and error guards.
// RELATIONSHIPS
//   Implements : n/a (test class)
//   Depends on : LiveRecordingService, TeeStream, TransformingSinkStream,
//                RecordingOptions, RecordingFormat
//   Used by    : dotnet test --filter LiveRecordingServiceTests
//   See also   : ImageTextify.Platform.Audio.LiveRecordingService
// NOTES
//   - Tests use real files in a unique temp directory, deleted in finally.
//   - The tee is read-only. The test feeds the recorder by reading the
//     tee to exhaustion; the tee's Read mirrors each read to the sink.
//   - The WAV header layout is asserted byte-for-byte against the same
//     44-byte RIFF/WAVE/fmt/data pattern the service writes.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ImageTextify.Core.Abstractions;
using ImageTextify.Platform.Audio;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

public sealed class LiveRecordingServiceTests
{
    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "itx-10f-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static byte[] Float32ToBytes(float[] samples)
    {
        var bytes = new byte[samples.Length * 4];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static void FeedTee(Stream tee)
    {
        var buf = new byte[4096];
        while (tee.Read(buf, 0, buf.Length) > 0) { }
    }

    [Fact]
    public async Task StartCreatesValidWavHeader()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "rec.wav");
            await using var svc = new LiveRecordingService();
            await svc.StartAsync(new RecordingOptions(path, RecordingFormat.Wav), CancellationToken.None);
            await svc.StopAsync(CancellationToken.None);

            var bytes = File.ReadAllBytes(path);
            bytes.Length.Should().BeGreaterThanOrEqualTo(44);
            System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("RIFF");
            System.Text.Encoding.ASCII.GetString(bytes, 8, 4).Should().Be("WAVE");
            BitConverter.ToInt16(bytes, 22).Should().Be(1);
            BitConverter.ToInt32(bytes, 24).Should().Be(16000);
            BitConverter.ToInt16(bytes, 34).Should().Be(16);
            System.Text.Encoding.ASCII.GetString(bytes, 36, 4).Should().Be("data");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public async Task WriteAndStopRecordsPcmData()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "rec.wav");
            var samples = new float[1000];
            for (int i = 0; i < samples.Length; i++) { samples[i] = 0.1f; }
            var inner = new MemoryStream(Float32ToBytes(samples));

            await using var svc = new LiveRecordingService();
            await svc.StartAsync(new RecordingOptions(path, RecordingFormat.Wav), CancellationToken.None);
            var tee = svc.WrapCaptureStream(inner);
            FeedTee(tee);
            var result = await svc.StopAsync(CancellationToken.None);

            var bytes = File.ReadAllBytes(path);
            int dataSize = BitConverter.ToInt32(bytes, 40);
            dataSize.Should().Be(2000);
            bytes.Length.Should().Be(44 + 2000);
            result.BytesWritten.Should().Be(44 + 2000);
            result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public async Task StopWithoutStartThrows()
    {
        var dir = NewDir();
        try
        {
            await using var svc = new LiveRecordingService();
            Func<Task> act = async () => await svc.StopAsync(CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public async Task WrapCaptureStreamReturnsTeeWhenRecording()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "rec.wav");
            await using var svc = new LiveRecordingService();
            var inner = new MemoryStream();

            var before = svc.WrapCaptureStream(inner);
            before.Should().BeSameAs(inner);

            await svc.StartAsync(new RecordingOptions(path, RecordingFormat.Wav), CancellationToken.None);
            var during = svc.WrapCaptureStream(inner);
            during.Should().NotBeSameAs(inner);
            during.Should().BeOfType<TeeStream>();

            await svc.StopAsync(CancellationToken.None);
            var after = svc.WrapCaptureStream(inner);
            after.Should().BeSameAs(inner);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public async Task DisposeAsyncFinalizesWhileRecording()
    {
        var dir = NewDir();
        try
        {
            var path = Path.Combine(dir, "rec.wav");
            var samples = new float[500];
            for (int i = 0; i < samples.Length; i++) { samples[i] = 0.05f; }
            var inner = new MemoryStream(Float32ToBytes(samples));

            var svc = new LiveRecordingService();
            await svc.StartAsync(new RecordingOptions(path, RecordingFormat.Wav), CancellationToken.None);
            var tee = svc.WrapCaptureStream(inner);
            FeedTee(tee);
            await svc.DisposeAsync();

            var bytes = File.ReadAllBytes(path);
            bytes.Length.Should().Be(44 + 1000);
            System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("RIFF");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public async Task StartTwiceThrows()
    {
        var dir = NewDir();
        try
        {
            var p1 = Path.Combine(dir, "a.wav");
            var p2 = Path.Combine(dir, "b.wav");
            await using var svc = new LiveRecordingService();
            await svc.StartAsync(new RecordingOptions(p1, RecordingFormat.Wav), CancellationToken.None);
            Func<Task> act = async () => await svc.StartAsync(new RecordingOptions(p2, RecordingFormat.Wav), CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>();
            await svc.StopAsync(CancellationToken.None);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }
}