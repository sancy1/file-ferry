// filepath: src/FileHarvester.Tests/ImageTextify/NAudioAudioCaptureServiceTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.ImageTextify
// since:    Phase 10 Block 10.A
// purpose:  Behavior tests for NAudioAudioCaptureService. Verifies
//           argument validation and unknown-device rejection without
//           requiring a real microphone. Real hardware validation is
//           Gate 10.3.
//
// RELATIONSHIPS
//   Implements : n/a (test file)
//   Depends on : NAudioAudioCaptureService
//   Used by    : dotnet test --filter NAudioAudioCaptureServiceTests
//   See also   : src/ImageTextify.Platform/Audio/NAudioAudioCaptureService.cs
//
// NOTES
//   - Tests do not open a microphone. They assert the service rejects
//     bad arguments and unknown device ids. Real device open is tested
//     at Gate 10.3 by the manual run.
//   - The test class is marked [SupportedOSPlatform("windows")] because
//     NAudioAudioCaptureService is Windows-only. The tests run on any
//     platform (the analyzer accepts the annotation), but the adapter
//     will throw on non-Windows at Gate 10.3.

using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using ImageTextify.Core.Abstractions;
using ImageTextify.Platform.Audio;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

[SupportedOSPlatform("windows")]
public sealed class NAudioAudioCaptureServiceTests
{
    [Fact]
    public async Task StartAsyncRejectsNullOptions()
    {
        var service = new NAudioAudioCaptureService();
        Func<Task> act = async () => await service.StartAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task StartAsyncRejectsUnknownDeviceId()
    {
        var service = new NAudioAudioCaptureService();
        var options = new AudioCaptureOptions(
            DeviceId: "no-such-device-" + Guid.NewGuid().ToString("N"));
        Func<Task> act = async () => await service.StartAsync(options, CancellationToken.None);
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task StartAsyncRejectsCancelledToken()
    {
        var service = new NAudioAudioCaptureService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var options = new AudioCaptureOptions();
        Func<Task> act = async () => await service.StartAsync(options, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}