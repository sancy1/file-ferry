// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Dispatch/DispatchingOutputSinkTests.cs
// PURPOSE:  Unit tests for DispatchingOutputSink. Verifies destination
//           routing: null, local path, file://, cloud scheme.
// ============================================================================

using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Dispatch;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud.Dispatch;

public sealed class DispatchingOutputSinkTests
{
    private static ComposedDocument EmptyDoc() => new(
        Files: Array.Empty<FileContent>(),
        Mode: OutputMode.SingleMerged,
        DelimiterTemplate: string.Empty,
        TwoBlankLines: false,
        IncludeTableOfContents: false,
        IncludeManifest: false);

    private static OutputSinkOptions Options(string? folder) => new(
        DestinationFolder: folder,
        Extension: ".txt");

    [Fact]
    public async Task SaveLocalDestinationRoutesToLocalSink()
    {
        var local = new StubLocalOutputSink();
        var cloud = new StubCloudOutputSink();
        var sut = new DispatchingOutputSink(local, cloud);

        await sut.SaveAsync(EmptyDoc(), Options(@"C:\out"));

        local.SaveCalls.Should().Be(1);
        cloud.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task SaveCloudDestinationRoutesToCloudSink()
    {
        var local = new StubLocalOutputSink();
        var cloud = new StubCloudOutputSink();
        var sut = new DispatchingOutputSink(local, cloud);

        await sut.SaveAsync(EmptyDoc(), Options("s3://bucket"));

        local.SaveCalls.Should().Be(0);
        cloud.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task SaveNullDestinationRoutesToLocalSink()
    {
        var local = new StubLocalOutputSink();
        var cloud = new StubCloudOutputSink();
        var sut = new DispatchingOutputSink(local, cloud);

        await sut.SaveAsync(EmptyDoc(), Options(null));

        local.SaveCalls.Should().Be(1);
        cloud.SaveCalls.Should().Be(0);
    }

    [Fact]
    public async Task SaveFileUriDestinationRoutesToLocalSink()
    {
        var local = new StubLocalOutputSink();
        var cloud = new StubCloudOutputSink();
        var sut = new DispatchingOutputSink(local, cloud);

        await sut.SaveAsync(EmptyDoc(), Options("file:///C:/tmp"));

        local.SaveCalls.Should().Be(1);
        cloud.SaveCalls.Should().Be(0);
    }
}
