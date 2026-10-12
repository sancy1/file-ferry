// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/Providers/GDriveClientAdapterTests.cs
// PURPOSE:  Deterministic tests for GDriveClientAdapter against a stub
//           IGDriveTransport. No Google service is contacted. Verifies the
//           exact folder query, page token and page size forwarding, upload
//           and update metadata and stream forwarding, the update-versus-
//           create distinction, not-found conversion, cancellation and
//           non-404 propagation, export MIME forwarding, and null
//           timestamp preservation.
// RELATION: Exercises FileHarvester.Platform.Cloud.Providers.GDriveClientAdapter.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Platform.Cloud.Providers;
using FluentAssertions;
using Google;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Upload;
using NSubstitute;
using Xunit;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace FileHarvester.Tests.Cloud.Providers;

/// <summary>
/// Tests for <see cref="GDriveClientAdapter"/> over a stub transport.
/// </summary>
public sealed class GDriveClientAdapterTests
{
    private const string FolderId = "the-folder-id";
    private const string FileId = "the-file-id";

    // The apostrophe is built from its character code so the expected
    // query is constructed without embedding a quote in a string literal.
    private static string ExpectedQuery()
        => (char)39 + FolderId + (char)39 + " in parents and trashed = false";

    private static GDriveClientAdapter Build(IGDriveTransport transport)
        => new(transport);

    private static DriveFile SampleFile(string id, string? mime, long? size, DateTimeOffset? modified, bool? trashed)
        => new()
        {
            Id = id,
            Name = "sample",
            MimeType = mime,
            Size = size,
            ModifiedTimeDateTimeOffset = modified,
            Trashed = trashed,
        };

    [Fact]
    public async Task ListAsyncBuildsExactFolderQuery()
    {
        var transport = Substitute.For<IGDriveTransport>();
        transport.ListAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FileList { Files = new List<DriveFile>() }));

        var sut = Build(transport);
        await sut.ListAsync(FolderId, null, 50, CancellationToken.None);

        await transport.Received(1).ListAsync(ExpectedQuery(), 50, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsyncForwardsPageSizeAndToken()
    {
        var transport = Substitute.For<IGDriveTransport>();
        transport.ListAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FileList { Files = new List<DriveFile>(), NextPageToken = "next" }));

        var sut = Build(transport);
        var page = await sut.ListAsync(FolderId, "tok-1", 100, CancellationToken.None);

        await transport.Received(1).ListAsync(ExpectedQuery(), 100, "tok-1", Arg.Any<CancellationToken>());
        page.ContinuationToken.Should().Be("next");
    }

    [Fact]
    public async Task ListAsyncRejectsNonPositiveMax()
    {
        var transport = Substitute.For<IGDriveTransport>();
        var sut = Build(transport);

        var act = async () => await sut.ListAsync(FolderId, null, 0, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ListAsyncMapsFilesToMetadata()
    {
        var when = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var transport = Substitute.For<IGDriveTransport>();
        transport.ListAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FileList
            {
                Files = new List<DriveFile>
                {
                    SampleFile("a", "text/plain", 100, when, false),
                },
            }));

        var sut = Build(transport);
        var page = await sut.ListAsync(FolderId, null, 10, CancellationToken.None);

        page.Items.Should().HaveCount(1);
        page.Items[0].Id.Should().Be("a");
        page.Items[0].SizeBytes.Should().Be(100);
        page.Items[0].LastModifiedUtc.Should().Be(when.UtcDateTime);
        page.Items[0].IsTrashed.Should().BeFalse();
    }

    [Fact]
    public async Task UploadForwardsMetadataAndContentType()
    {
        var transport = Substitute.For<IGDriveTransport>();
        transport.UploadAsync(Arg.Any<DriveFile>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IUploadProgress>(new StubProgress(UploadStatus.Completed)));

        var sut = Build(transport);
        using var content = new MemoryStream(new byte[] { 1, 2, 3 });
        await sut.UploadAsync("new-name.txt", "parent-id", "text/plain", content, CancellationToken.None);

        await transport.Received(1).UploadAsync(
            Arg.Is<DriveFile>(f => f.Name == "new-name.txt" && f.Parents.Contains("parent-id")),
            Arg.Any<Stream>(),
            "text/plain",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsyncForwardsExactFileIdAndDoesNotCreate()
    {
        var transport = Substitute.For<IGDriveTransport>();
        transport.UpdateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IUploadProgress>(new StubProgress(UploadStatus.Completed)));

        var sut = Build(transport);
        using var content = new MemoryStream(new byte[] { 9, 8, 7 });
        await sut.UpdateAsync(FileId, "text/plain", content, CancellationToken.None);

        // The update targets the exact file id.
        await transport.Received(1).UpdateAsync(FileId, "text/plain", Arg.Any<Stream>(), Arg.Any<CancellationToken>());

        // It does not create a new file.
        await transport.DidNotReceive().UploadAsync(
            Arg.Any<DriveFile>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsyncRejectsEmptyFileId()
    {
        var transport = Substitute.For<IGDriveTransport>();
        var sut = Build(transport);

        using var content = new MemoryStream();
        var act = async () => await sut.UpdateAsync("", "text/plain", content, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task FailedUploadProgressBecomesException()
    {
        var transport = Substitute.For<IGDriveTransport>();
        transport.UploadAsync(Arg.Any<DriveFile>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IUploadProgress>(new StubProgress(UploadStatus.Failed, new InvalidOperationException("upload failed"))));

        var sut = Build(transport);
        using var content = new MemoryStream();
        var act = async () => await sut.UploadAsync("n.txt", null, "text/plain", content, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task FailedUpdateProgressBecomesException()
    {
        var transport = Substitute.For<IGDriveTransport>();
        transport.UpdateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IUploadProgress>(new StubProgress(UploadStatus.Failed, new InvalidOperationException("update failed"))));

        var sut = Build(transport);
        using var content = new MemoryStream();
        var act = async () => await sut.UpdateAsync(FileId, "text/plain", content, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExistsAsyncReturnsFalseOnGenuineNotFound()
    {
        var transport = Substitute.For<IGDriveTransport>();
        transport.GetAsync(FileId, Arg.Any<CancellationToken>())
            .Returns<Task<DriveFile>>(_ => throw NotFound());

        var sut = Build(transport);
        var exists = await sut.ExistsAsync(FileId, CancellationToken.None);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsyncPropagatesNonNotFound()
    {
        var transport = Substitute.For<IGDriveTransport>();
        transport.GetAsync(FileId, Arg.Any<CancellationToken>())
            .Returns<Task<DriveFile>>(_ => throw Forbidden());

        var sut = Build(transport);
        var act = async () => await sut.ExistsAsync(FileId, CancellationToken.None);

        await act.Should().ThrowAsync<GoogleApiException>();
    }

    [Fact]
    public async Task DeleteIfExistsAsyncReturnsFalseOnGenuineNotFound()
    {
        var transport = Substitute.For<IGDriveTransport>();
        transport.DeleteAsync(FileId, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw NotFound());

        var sut = Build(transport);
        var deleted = await sut.DeleteIfExistsAsync(FileId, CancellationToken.None);

        deleted.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteIfExistsAsyncPropagatesNonNotFound()
    {
        var transport = Substitute.For<IGDriveTransport>();
        transport.DeleteAsync(FileId, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw Forbidden());

        var sut = Build(transport);
        var act = async () => await sut.DeleteIfExistsAsync(FileId, CancellationToken.None);

        await act.Should().ThrowAsync<GoogleApiException>();
    }

    [Fact]
    public async Task CancellationPropagatesUnchanged()
    {
        var transport = Substitute.For<IGDriveTransport>();
        transport.GetAsync(FileId, Arg.Any<CancellationToken>())
            .Returns<Task<DriveFile>>(_ => throw new OperationCanceledException());

        var sut = Build(transport);
        var act = async () => await sut.ExistsAsync(FileId, CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExportForwardsMimeType()
    {
        var transport = Substitute.For<IGDriveTransport>();
        transport.ExportAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sut = Build(transport);
        await using var stream = await sut.ExportAsync(FileId, "application/pdf", CancellationToken.None);

        await transport.Received(1).ExportAsync(FileId, "application/pdf", Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NullModifiedTimestampStaysNull()
    {
        var transport = Substitute.For<IGDriveTransport>();
        transport.GetAsync(FileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SampleFile(FileId, "text/plain", null, null, null)));

        var sut = Build(transport);
        var meta = await sut.GetPropertiesAsync(FileId, CancellationToken.None);

        meta.LastModifiedUtc.Should().BeNull();
        meta.SizeBytes.Should().Be(0);
        meta.IsTrashed.Should().BeFalse();
    }

    [Fact]
    public async Task MissingFileIdFromSdkThrows()
    {
        var transport = Substitute.For<IGDriveTransport>();
        transport.GetAsync(FileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SampleFile(string.Empty, "text/plain", 1, null, false)));

        var sut = Build(transport);
        var act = async () => await sut.GetPropertiesAsync(FileId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---- Helpers -------------------------------------------------------

    private static GoogleApiException NotFound()
    {
        var ex = new GoogleApiException("drive", "not found");
        ex.HttpStatusCode = HttpStatusCode.NotFound;
        return ex;
    }

    private static GoogleApiException Forbidden()
    {
        var ex = new GoogleApiException("drive", "forbidden");
        ex.HttpStatusCode = HttpStatusCode.Forbidden;
        return ex;
    }

    private sealed class StubProgress : IUploadProgress
    {
        public StubProgress(UploadStatus status, Exception? exception = null)
        {
            Status = status;
            Exception = exception;
        }

        public UploadStatus Status { get; }
        public long BytesSent => 0;
        public Exception? Exception { get; }
    }
}
