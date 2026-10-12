using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Platform.Cloud.OAuth;
using FileHarvester.Platform.Cloud.Services;
using FluentAssertions;
using Google;
using NSubstitute;

namespace FileHarvester.Tests.Cloud.Services;

public sealed class GoogleDrivePickerDownloadServiceTests
{
    private static OAuthClientConfig Config() => new(
        "gdrive",
        "client",
        "https://accounts.google.com/o/oauth2/v2/auth",
        "https://oauth2.googleapis.com/token",
        new[] { OAuthScopes.DriveFile },
        "http://127.0.0.1:53682/callback/");

    [Fact]
    public async Task SelectionChecksPermissionIdWithBothTokensAndUsesPickerTokenForAllFileOperations()
    {
        var clientFactory = new RecordingClientFactory();
        clientFactory.PermissionByToken["connected-token"] = "permission-1";
        clientFactory.PermissionByToken["picker-token"] = "permission-1";
        clientFactory.Metadata["binary-id"] = new("picture.png", "image/png", 4, true);
        clientFactory.Metadata["doc-id"] = new("briefing", "application/vnd.google-apps.document", null, true);
        clientFactory.Metadata["sheet-id"] = new("budget", "application/vnd.google-apps.spreadsheet", null, true);
        clientFactory.Metadata["slide-id"] = new("deck", "application/vnd.google-apps.presentation", null, true);
        var sut = CreateService(clientFactory, new List<string> { "binary-id", "doc-id", "sheet-id", "slide-id" });

        var selections = await sut.SelectFilesAsync("Personal");

        selections.Select(f => f.FileName).Should().Equal("picture.png", "briefing.pdf", "budget.xlsx", "deck.pptx");
        selections.All(f => f.Error is null && !string.IsNullOrWhiteSpace(f.DownloadHandle))
            .Should().BeTrue();
        clientFactory.PermissionLookups.Should().Contain(("connected-token", "permission-1"));
        clientFactory.PermissionLookups.Should().Contain(("picker-token", "permission-1"));
        clientFactory.MetadataLookups.Should().NotBeEmpty();
        clientFactory.MetadataLookups.Should().OnlyContain(r => r.Token == "picker-token");

        var expectedExports = new[]
        {
            (Id: "doc-id", Mime: "application/pdf"),
            (Id: "sheet-id", Mime: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            (Id: "slide-id", Mime: "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
        };
        foreach (var selection in selections)
        {
            await using var output = new MemoryStream();
            await sut.DownloadAsync(selection.DownloadHandle, output);
        }

        clientFactory.FileRequests.Should().Contain(("picker-token", "binary-id", "download", null));
        foreach (var item in expectedExports)
        {
            clientFactory.FileRequests.Should().Contain(("picker-token", item.Id, "export", item.Mime));
        }
        clientFactory.FileRequests.Should().NotContain(request => request.Token == "connected-token");
    }

    [Theory]
    [InlineData(null, "permission-1")]
    [InlineData("permission-1", null)]
    [InlineData("permission-1", "permission-2")]
    public async Task SelectionFailsClosedWhenAccountPermissionIdsCannotBeVerified(
        string? connectedId,
        string? pickerId)
    {
        var clientFactory = new RecordingClientFactory();
        clientFactory.PermissionByToken["connected-token"] = connectedId;
        clientFactory.PermissionByToken["picker-token"] = pickerId;
        var sut = CreateService(clientFactory, new List<string> { "file-id" });

        var act = () => sut.SelectFilesAsync("Personal");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not match*");
        clientFactory.FileRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectionReportsCannotDownloadForThatFileAndContinues()
    {
        var clientFactory = new RecordingClientFactory();
        clientFactory.PermissionByToken["connected-token"] = "permission-1";
        clientFactory.PermissionByToken["picker-token"] = "permission-1";
        clientFactory.Metadata["blocked"] = new("blocked.pdf", "application/pdf", 10, false);
        clientFactory.Metadata["allowed"] = new("allowed.txt", "text/plain", 8, true);
        var sut = CreateService(clientFactory, new List<string> { "blocked", "allowed" });

        var results = await sut.SelectFilesAsync("Personal");

        results.Should().HaveCount(2);
        results[0].Error.Should().Contain("does not allow");
        results[0].DownloadHandle.Should().BeEmpty();
        results[1].Error.Should().BeNull();
        results[1].FileName.Should().Be("allowed.txt");
    }

    [Fact]
    public async Task DownloadReportsProgressAndHandleCanOnlyBeUsedOnce()
    {
        var clientFactory = NewMatchedClientFactory();
        clientFactory.Metadata["file"] = new("archive.zip", "application/zip", 3, true);
        clientFactory.DownloadBytes = new byte[] { 1, 2, 3 };
        var sut = CreateService(clientFactory, new List<string> { "file" });
        var selection = (await sut.SelectFilesAsync("Personal")).Single();
        var progress = new InlineProgress<GoogleDriveDownloadProgress>();
        await using var output = new MemoryStream();

        await sut.DownloadAsync(selection.DownloadHandle, output, progress);
        output.ToArray().Should().Equal(new byte[] { 1, 2, 3 });
        progress.Values.Should().ContainSingle();
        progress.Values[0].BytesWritten.Should().Be(3);
        progress.Values[0].TotalBytes.Should().Be(3);

        var secondUse = () => sut.DownloadAsync(selection.DownloadHandle, Stream.Null);
        await secondUse.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already used*");
    }

    [Fact]
    public async Task ExpiredSelectionHandleCannotBeDownloaded()
    {
        var now = DateTime.UtcNow;
        var clientFactory = NewMatchedClientFactory();
        clientFactory.Metadata["file"] = new("notes.txt", "text/plain", 3, true);
        var sut = CreateService(clientFactory, new List<string> { "file" }, () => now);
        var selection = (await sut.SelectFilesAsync("Personal")).Single();
        now = now.AddMinutes(11);

        var act = () => sut.DownloadAsync(selection.DownloadHandle, Stream.Null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*expired*");
    }

    [Fact]
    public async Task GoogleApiDownloadFailureReturnsSanitizedActionableError()
    {
        var clientFactory = NewMatchedClientFactory();
        clientFactory.Metadata["file"] = new("notes.txt", "text/plain", 3, true);
        clientFactory.DownloadException = new GoogleApiException("Drive", "secret-like-detail")
        {
            HttpStatusCode = HttpStatusCode.Forbidden,
        };
        var sut = CreateService(clientFactory, new List<string> { "file" });
        var selection = (await sut.SelectFilesAsync("Personal")).Single();

        var act = () => sut.DownloadAsync(selection.DownloadHandle, Stream.Null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*denied access*")
            .WithMessage("*sharing*");
    }

    private static GoogleDrivePickerDownloadService CreateService(
        RecordingClientFactory clientFactory,
        IReadOnlyList<string> fileIds,
        Func<DateTime>? now = null)
    {
        var configStore = Substitute.For<IOAuthClientConfigStore>();
        configStore.ReadAsync("gdrive", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<OAuthClientConfig?>(Config()));
        return new GoogleDrivePickerDownloadService(
            configStore,
            (_, _, _) => Task.FromResult("connected-token"),
            new StubPickerFlow(new GoogleDrivePickerGrant(
                "picker-token",
                (now?.Invoke() ?? DateTime.UtcNow).AddMinutes(5),
                fileIds)),
            clientFactory.Create,
            now);
    }

    private static RecordingClientFactory NewMatchedClientFactory()
    {
        var factory = new RecordingClientFactory();
        factory.PermissionByToken["connected-token"] = "permission-1";
        factory.PermissionByToken["picker-token"] = "permission-1";
        return factory;
    }

    private sealed class StubPickerFlow(GoogleDrivePickerGrant grant) : IGoogleDrivePickerOAuthFlow
    {
        public Task<GoogleDrivePickerGrant> AuthorizeAsync(OAuthClientConfig config, CancellationToken ct) =>
            Task.FromResult(grant);
    }

    private sealed class RecordingClientFactory
    {
        public Dictionary<string, string?> PermissionByToken { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, GoogleDrivePickerFileMetadata> Metadata { get; } = new(StringComparer.Ordinal);
        public List<(string Token, string PermissionId)> PermissionLookups { get; } = new();
        public List<(string Token, string FileId)> MetadataLookups { get; } = new();
        public List<(string Token, string Id, string Operation, string? Mime)> FileRequests { get; } = new();
        public byte[] DownloadBytes { get; set; } = { 1, 2, 3 };
        public Exception? DownloadException { get; set; }

        public IGoogleDrivePickerFileClient Create(string token) => new RecordingClient(this, token);

        private sealed class RecordingClient(
            RecordingClientFactory owner,
            string token) : IGoogleDrivePickerFileClient
        {
            public Task<string?> GetCurrentUserPermissionIdAsync(CancellationToken ct)
            {
                var id = owner.PermissionByToken.GetValueOrDefault(token);
                if (id is not null) owner.PermissionLookups.Add((token, id));
                return Task.FromResult(id);
            }

            public Task<GoogleDrivePickerFileMetadata> GetMetadataAsync(string fileId, CancellationToken ct)
            {
                owner.MetadataLookups.Add((token, fileId));
                return Task.FromResult(owner.Metadata[fileId]);
            }

            public async Task DownloadAsync(string fileId, Stream destination, CancellationToken ct)
            {
                owner.FileRequests.Add((token, fileId, "download", null));
                if (owner.DownloadException is not null) throw owner.DownloadException;
                await destination.WriteAsync(owner.DownloadBytes, ct);
            }

            public async Task ExportAsync(string fileId, string mimeType, Stream destination, CancellationToken ct)
            {
                owner.FileRequests.Add((token, fileId, "export", mimeType));
                await destination.WriteAsync(owner.DownloadBytes, ct);
            }

            public void Dispose() { }
        }
    }

    private sealed class InlineProgress<T>(List<T> values) : IProgress<T>
    {
        public InlineProgress() : this(new List<T>()) { }
        public List<T> Values { get; } = values;
        public void Report(T value) => Values.Add(value);
    }
}
