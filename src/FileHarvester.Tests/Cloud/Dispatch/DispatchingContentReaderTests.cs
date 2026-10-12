// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Dispatch/DispatchingContentReaderTests.cs
// PURPOSE:  Unit tests for DispatchingContentReader. Verifies routing,
//           order preservation, and the missing-result placeholder.
// ============================================================================

using System.Text;
using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Dispatch;
using FileHarvester.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Cloud.Dispatch;

public sealed class DispatchingContentReaderTests
{
    private static ResolvedFile MakeResolved(string path, int index)
    {
        var req = new FileRequest(Index: index, RawInput: path);
        return new ResolvedFile(
            Request: req,
            AbsolutePath: path,
            SizeBytes: 1,
            LastModifiedUtc: DateTime.UtcNow,
            Confidence: ResolutionConfidence.Searched);
    }

    private static FileContent MakeContent(ResolvedFile f)
    {
        var bytes = Encoding.UTF8.GetBytes("x");
        return new FileContent(
            File: f,
            RawBytes: bytes,
            DecodedText: "x",
            Encoding: new UTF8Encoding(false),
            IsBinary: false,
            Sha256: "0");
    }

    [Fact]
    public async Task ReadManyEmptyBatchReturnsEmpty()
    {
        var local = new StubLocalContentReader();
        var cloud = new StubCloudContentReader();
        var sut = new DispatchingContentReader(local, cloud);

        var result = await sut.ReadManyAsync(Array.Empty<ResolvedFile>());

        result.Should().BeEmpty();
        local.Batches.Should().BeEmpty();
        cloud.Batches.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadManyLocalOnlyCallsLocalReaderOnly()
    {
        var local = new StubLocalContentReader();
        var cloud = new StubCloudContentReader();
        local.OnReadMany = fs => Task.FromResult<IReadOnlyList<FileContent>>(
            fs.Select(MakeContent).ToList());
        var sut = new DispatchingContentReader(local, cloud);

        var files = new[]
        {
            MakeResolved(@"C:\a.txt", 0),
            MakeResolved(@"C:\b.txt", 1),
        };

        var result = await sut.ReadManyAsync(files);

        result.Should().HaveCount(2);
        local.Batches.Should().HaveCount(1);
        cloud.Batches.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadManyCloudOnlyCallsCloudReaderOnly()
    {
        var local = new StubLocalContentReader();
        var cloud = new StubCloudContentReader();
        cloud.OnReadMany = fs => Task.FromResult<IReadOnlyList<FileContent>>(
            fs.Select(MakeContent).ToList());
        var sut = new DispatchingContentReader(local, cloud);

        var files = new[]
        {
            MakeResolved("s3://bucket/a.txt", 0),
            MakeResolved("s3://bucket/b.txt", 1),
        };

        var result = await sut.ReadManyAsync(files);

        result.Should().HaveCount(2);
        local.Batches.Should().BeEmpty();
        cloud.Batches.Should().HaveCount(1);
    }

    [Fact]
    public async Task ReadManyMixedBatchReturnsOnePerInputInOrder()
    {
        var local = new StubLocalContentReader();
        var cloud = new StubCloudContentReader();
        local.OnReadMany = fs => Task.FromResult<IReadOnlyList<FileContent>>(
            fs.Select(MakeContent).ToList());
        cloud.OnReadMany = fs => Task.FromResult<IReadOnlyList<FileContent>>(
            fs.Select(MakeContent).ToList());
        var sut = new DispatchingContentReader(local, cloud);

        var files = new[]
        {
            MakeResolved(@"C:\a.txt", 0),
            MakeResolved("s3://bucket/b.txt", 1),
            MakeResolved(@"C:\c.txt", 2),
        };

        var result = await sut.ReadManyAsync(files);

        result.Should().HaveCount(3);
        result[0].File.Request.Index.Should().Be(0);
        result[1].File.Request.Index.Should().Be(1);
        result[2].File.Request.Index.Should().Be(2);
    }

    [Fact]
    public async Task ReadManyMissingResultProducesPlaceholder()
    {
        var local = new StubLocalContentReader();
        var cloud = new StubCloudContentReader();
        // The local reader returns only one result for two input files.
        local.OnReadMany = fs =>
        {
            var list = new List<FileContent> { MakeContent(fs[0]) };
            return Task.FromResult<IReadOnlyList<FileContent>>(list);
        };
        var sut = new DispatchingContentReader(local, cloud);

        var files = new[]
        {
            MakeResolved(@"C:\a.txt", 0),
            MakeResolved(@"C:\b.txt", 1),
        };

        var result = await sut.ReadManyAsync(files);

        result.Should().HaveCount(2);
        result[0].IsUnreadable.Should().BeFalse();
        result[1].IsUnreadable.Should().BeTrue();
        result[1].ReadError.Should().Be(DispatchingContentReader.MissingResultReason);
    }
}
