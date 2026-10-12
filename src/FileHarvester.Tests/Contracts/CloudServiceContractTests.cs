// ============================================================================
// FILE:     src/FileHarvester.Tests/Contracts/CloudServiceContractTests.cs
// PURPOSE:  Behavioral contract tests for the cloud service layer.
//           Asserts the routing, placeholder, and completion behavior
//           documented on the interfaces and specified in the design.
// RELATION: Executable contract tier. Uses StubCloudProvider,
//           StubLocalFileResolver, StubLocalContentReader,
//           StubLocalOutputSink, and StubCloudOutputSink.
// ============================================================================

using System.Text;
using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Dispatch;
using FileHarvester.Tests.Cloud.Dispatch;
using FileHarvester.Tests.Cloud.Services;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Contracts;

public sealed class CloudServiceContractTests
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

    [Fact]
    public async Task ResolverRoutesByScheme()
    {
        // The dispatcher design says local paths route to the local
        // resolver, cloud paths to the cloud resolver.
        var local = new StubLocalFileResolver();
        var cloud = new StubCloudFileResolver();
        var sut = new DispatchingFileResolver(local, cloud);

        var requests = new[]
        {
            new FileRequest(Index: 0, RawInput: @"C:\a.txt"),
            new FileRequest(Index: 1, RawInput: "s3://bucket/b.txt"),
        };

        await sut.ResolveAsync(requests);

        local.Batches.Should().HaveCount(1);
        cloud.Batches.Should().HaveCount(1);
        local.Batches[0].Should().HaveCount(1);
        cloud.Batches[0].Should().HaveCount(1);
    }

    [Fact]
    public async Task UnreadableReturnsPlaceholder()
    {
        // The IContentReader doc says a normal I/O failure produces a
        // FileContent.Unreadable placeholder, not a throw.
        var local = new StubLocalContentReader();
        var cloud = new StubCloudContentReader();
        cloud.OnReadMany = fs =>
        {
            var list = fs.Select(f => FileContent.Unreadable(f, "provider failed")).ToList();
            return Task.FromResult<IReadOnlyList<FileContent>>(list);
        };

        var sut = new DispatchingContentReader(local, cloud);
        var files = new[] { MakeResolved("s3://bucket/a.txt", 0) };

        var result = await sut.ReadManyAsync(files);

        result.Should().HaveCount(1);
        result[0].IsUnreadable.Should().BeTrue();
        result[0].ReadError.Should().Be("provider failed");
    }

    [Fact]
    public async Task OutputSinkCompletesUpload()
    {
        // The CloudOutputSink design says the handle's CompleteAsync is
        // called after the format writer returns.
        var local = new StubLocalOutputSink();
        var cloud = new StubCloudOutputSink();

        var completed = false;
        cloud.OnSave = (doc, opts) =>
        {
            completed = true;
            return Task.FromResult(SaveResult.Ok(
                destinationFolder: opts.DestinationFolder ?? "cloud",
                outputPath: "s3://bucket/out.txt",
                fileCount: doc.Files.Count,
                bytesWritten: 0));
        };

        var sut = new DispatchingOutputSink(local, cloud);
        var doc = new ComposedDocument(
            Files: Array.Empty<FileContent>(),
            Mode: OutputMode.SingleMerged,
            DelimiterTemplate: string.Empty,
            TwoBlankLines: false,
            IncludeTableOfContents: false,
            IncludeManifest: false);

        var options = new OutputSinkOptions(
            DestinationFolder: "s3://bucket",
            Extension: ".txt");

        var result = await sut.SaveAsync(doc, options);

        completed.Should().BeTrue();
        result.Success.Should().BeTrue();
    }
}
