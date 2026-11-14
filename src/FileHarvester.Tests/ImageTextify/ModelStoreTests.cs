// ============================================================================
// FILE:     src/FileHarvester.Tests/ImageTextify/ModelStoreTests.cs
// PURPOSE:  Behavior tests for ModelStore. Uses a real test-scoped
//           ServiceProvider and a LocalModelServer bound to 127.0.0.1.
//           Never contacts a remote URL. Uses NSubstitute for the
//           IFileHasher seam so IOException and UnauthorizedAccessException
//           paths are deterministic on all platforms.
// RELATION: Phase 2.2 Block C exit-criteria test.
// ============================================================================

using System.Net.Http;
using System.Security.Cryptography;
using FileHarvester.Core.Composition;
using FileHarvester.Tests.Infrastructure;
using FluentAssertions;
using ImageTextify.Core.Abstractions;
using ImageTextify.Core.Models;
using ImageTextify.Platform.Composition;
using ImageTextify.Platform.Models;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Abstractions;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Behavior tests for ModelStore.
/// </summary>
public sealed class ModelStoreTests : IDisposable
{
    private const string HttpClientName = "ImageTextifyModelDownload";

    private readonly string _root;
    private readonly ServiceProvider _provider;

    public ModelStoreTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "file-ferry-model-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var services = new ServiceCollection();

        services.AddFileHarvester(
            new ResilienceConfig(
                LogDirectory: Path.Combine(_root, "logs"),
                EnableConsoleSink: false,
                EnableFileSink: false),
            extraSink: null,
            configureImageTextifyPolicies: ImageTextifyComposition.RegisterResiliencePolicies);

        services.AddImageTextify(_root);

        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        try { _provider.Dispose(); } catch { }
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }

    // ------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------

    private static ImageTextifyModelDescriptor Descriptor(
        string url,
        long size,
        string hash,
        string id = "test-model")
        => new(
            Id: id,
            DisplayName: "Test Model",
            SourceUrl: url,
            SizeBytes: size,
            Sha256Hex: hash,
            LicenseName: "Apache-2.0",
            LicenseUrl: "https://www.apache.org/licenses/LICENSE-2.0",
            Purpose: "Test purpose.");

    private static string Sha256Hex(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private ModelStore BuildStore(
        IHttpClientFactory? factory = null,
        IFileHasher? hasher = null)
        => new(
            _provider.GetRequiredService<IResilienceExecutor>(),
            factory ?? _provider.GetRequiredService<IHttpClientFactory>(),
            hasher ?? new Sha256FileHasher(),
            () => _root);

    private string WriteFileAt(string relativePath, byte[] content)
    {
        var full = Path.Combine(_root, relativePath);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(full, content);
        return full;
    }

    private string FinalPathFor(ImageTextifyModelDescriptor d)
        => Path.Combine(_root, d.Id, "model.bin");

    // ------------------------------------------------------------------
    // 1. GetStatus returns NotDownloaded when no file exists.
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetStatusReturnsNotDownloadedWhenFileAbsent()
    {
        var store = BuildStore();
        var d = Descriptor(
            "https://example.com/x.onnx", 8, new string('0', 64));

        var status = await store.GetStatusAsync(d, CancellationToken.None);

        status.Should().Be(ImageTextifyModelStatus.NotDownloaded);
    }

    // ------------------------------------------------------------------
    // 2. GetStatus returns Downloaded when the file exists and its hash matches.
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetStatusReturnsDownloadedWhenFilePresentAndHashMatches()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var d = Descriptor("https://example.com/x.onnx", bytes.Length, Sha256Hex(bytes));
        WriteFileAt(Path.Combine(d.Id, "model.bin"), bytes);

        var store = BuildStore();
        var status = await store.GetStatusAsync(d, CancellationToken.None);

        status.Should().Be(ImageTextifyModelStatus.Downloaded);
    }

    // ------------------------------------------------------------------
    // 3. GetStatus returns Corrupted when the file exists but its hash differs.
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetStatusReturnsCorruptedWhenFilePresentAndHashMismatch()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var wrongHash = new string('a', 64);
        var d = Descriptor("https://example.com/x.onnx", bytes.Length, wrongHash);
        WriteFileAt(Path.Combine(d.Id, "model.bin"), bytes);

        var store = BuildStore();
        var status = await store.GetStatusAsync(d, CancellationToken.None);

        status.Should().Be(ImageTextifyModelStatus.Corrupted);
    }

    // ------------------------------------------------------------------
    // 4. GetStatus never contacts the network.
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetStatusDoesNotContactNetwork()
    {
        using var server = new LocalModelServer();
        server.SetPayload(new byte[] { 9, 9, 9 });

        var d = Descriptor(server.BaseUrl + "x.onnx", 3, Sha256Hex(new byte[] { 9, 9, 9 }));
        var store = BuildStore();

        _ = await store.GetStatusAsync(d, CancellationToken.None);

        server.RequestCount.Should().Be(0,
            "GetStatusAsync must not contact the network.");
    }

    // ------------------------------------------------------------------
    // 5. GetStatus returns Unavailable for IOException.
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetStatusReturnsUnavailableForIoFailure()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var d = Descriptor("https://example.com/x.onnx", bytes.Length, Sha256Hex(bytes));
        WriteFileAt(Path.Combine(d.Id, "model.bin"), bytes);

        var hasher = new ThrowingFileHasher(new IOException("simulated"));

        var store = BuildStore(hasher: hasher);
        var status = await store.GetStatusAsync(d, CancellationToken.None);

        status.Should().Be(ImageTextifyModelStatus.Unavailable);
    }

    // ------------------------------------------------------------------
    // 6. GetStatus returns Unavailable for UnauthorizedAccessException.
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetStatusReturnsUnavailableForUnauthorizedAccess()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var d = Descriptor("https://example.com/x.onnx", bytes.Length, Sha256Hex(bytes));
        WriteFileAt(Path.Combine(d.Id, "model.bin"), bytes);

        var hasher = new ThrowingFileHasher(new UnauthorizedAccessException("simulated"));

        var store = BuildStore(hasher: hasher);
        var status = await store.GetStatusAsync(d, CancellationToken.None);

        status.Should().Be(ImageTextifyModelStatus.Unavailable);
    }

    // ------------------------------------------------------------------
    // 7. EnsureAvailable returns the path when the file is already valid.
    // ------------------------------------------------------------------

    [Fact]
    public async Task EnsureAvailableReturnsPathWhenFileAlreadyPresentAndValid()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var d = Descriptor("https://example.com/x.onnx", bytes.Length, Sha256Hex(bytes));
        var expected = WriteFileAt(Path.Combine(d.Id, "model.bin"), bytes);

        var store = BuildStore();
        var returned = await store.EnsureAvailableAsync(
            d, progress: null, CancellationToken.None);

        returned.Should().Be(expected);
    }

    // ------------------------------------------------------------------
    // 8. No network call when the file is already valid.
    // ------------------------------------------------------------------

    [Fact]
    public async Task NoNetworkCallWhenFileIsAlreadyValid()
    {
        using var server = new LocalModelServer();
        var bytes = new byte[] { 5, 5, 5 };
        server.SetPayload(bytes);

        var d = Descriptor(server.BaseUrl + "x.onnx", bytes.Length, Sha256Hex(bytes));
        WriteFileAt(Path.Combine(d.Id, "model.bin"), bytes);

        var store = BuildStore();
        _ = await store.EnsureAvailableAsync(d, progress: null, CancellationToken.None);

        server.RequestCount.Should().Be(0,
            "a valid existing file must be returned without a network call.");
    }

    // ------------------------------------------------------------------
    // 9. EnsureAvailable downloads and verifies the hash.
    // ------------------------------------------------------------------

    [Fact]
    public async Task EnsureAvailableDownloadsAndVerifiesHash()
    {
        using var server = new LocalModelServer();
        var bytes = new byte[] { 10, 20, 30, 40, 50, 60 };
        server.SetPayload(bytes);

        var d = Descriptor(server.BaseUrl + "model.onnx", bytes.Length, Sha256Hex(bytes));
        var store = BuildStore();

        var path = await store.EnsureAvailableAsync(d, progress: null, CancellationToken.None);

        File.Exists(path).Should().BeTrue();
        var onDisk = File.ReadAllBytes(path);
        onDisk.Should().Equal(bytes);
        server.RequestCount.Should().BeGreaterThan(0);
    }

    // ------------------------------------------------------------------
    // 10. EnsureAvailable uses a .partial file and atomic rename.
    // ------------------------------------------------------------------

    [Fact]
    public async Task EnsureAvailableUsesPartialFileAndAtomicRename()
    {
        using var server = new LocalModelServer();
        var bytes = new byte[] { 7, 7, 7 };
        server.SetPayload(bytes);

        var d = Descriptor(server.BaseUrl + "model.onnx", bytes.Length, Sha256Hex(bytes));
        var store = BuildStore();

        var path = await store.EnsureAvailableAsync(d, progress: null, CancellationToken.None);

        File.Exists(path).Should().BeTrue();
        File.Exists(path + ".partial").Should().BeFalse(
            "the partial file must be promoted and removed on success.");
    }

    // ------------------------------------------------------------------
    // 11. EnsureAvailable deletes the .partial file on checksum mismatch.
    // ------------------------------------------------------------------

    [Fact]
    public async Task EnsureAvailableDeletesPartialFileOnChecksumMismatch()
    {
        using var server = new LocalModelServer();
        var served = new byte[] { 1, 2, 3, 4 };
        server.SetPayload(served);

        // Descriptor declares a different hash than the served bytes produce.
        var wrongHash = new string('b', 64);
        var d = Descriptor(server.BaseUrl + "model.onnx", served.Length, wrongHash);
        var store = BuildStore();

        var act = async () => await store.EnsureAvailableAsync(
            d, progress: null, CancellationToken.None);

        await act.Should().ThrowAsync<ModelDownloadException>();

        var final = FinalPathFor(d);
        File.Exists(final).Should().BeFalse();
        File.Exists(final + ".partial").Should().BeFalse(
            "the partial file must be deleted on checksum mismatch.");
    }

    // ------------------------------------------------------------------
    // 12. EnsureAvailable rejects a truncated download.
    // ------------------------------------------------------------------

    [Fact]
    public async Task EnsureAvailableRejectsTruncatedDownload()
    {
        using var server = new LocalModelServer();
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        server.SetPayload(bytes);
        server.TruncateTo(3);

        // The descriptor declares 8 bytes; the server will serve only 3.
        var d = Descriptor(server.BaseUrl + "model.onnx", bytes.Length, Sha256Hex(bytes));
        var store = BuildStore();

        var act = async () => await store.EnsureAvailableAsync(
            d, progress: null, CancellationToken.None);

        await act.Should().ThrowAsync<ModelDownloadException>(
            "a truncated download must be rejected by the byte-count check.");
    }

    // ------------------------------------------------------------------
    // 13. EnsureAvailable honors cancellation and leaves no partial file.
    // ------------------------------------------------------------------

    [Fact]
    public async Task EnsureAvailableHonorsCancellationAndLeavesNoPartialFile()
    {
        // Deterministic cancellation test using a fake HttpMessageHandler that
        // blocks the response until the cancellation token fires. Replaces the
        // prior 1ms cancellation approach that raced the local HTTP server.
        using var handler = new DelayHttpMessageHandler();
        var factory = new FakeHttpClientFactory(handler);
        var store = BuildStore(factory);

        var bytes = new byte[1024 * 64];
        for (var i = 0; i < bytes.Length; i++) { bytes[i] = (byte)(i & 0xFF); }
        var d = Descriptor("https://example.invalid/model.onnx", bytes.Length, Sha256Hex(bytes));

        using var cts = new CancellationTokenSource();
        var act = async () => await store.EnsureAvailableAsync(d, progress: null, cts.Token);

        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await act.Should().ThrowAsync<OperationCanceledException>();

        var final = FinalPathFor(d);
        File.Exists(final).Should().BeFalse();
        File.Exists(final + ".partial").Should().BeFalse(
            "cancellation must leave no partial file.");
    }

    /// <summary>
    /// Fake HttpMessageHandler that blocks the response until the token
    /// cancels. Simulates a slow server so cancellation tests are
    /// deterministic regardless of machine speed.
    /// </summary>
    private sealed class DelayHttpMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Array.Empty<byte>()),
            };
        }
    }

    /// <summary>
    /// Fake IHttpClientFactory that always returns an HttpClient with the
    /// given HttpMessageHandler. Required because BuildStore accepts
    /// IHttpClientFactory (not raw HttpClient).
    /// </summary>
    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeHttpClientFactory(HttpMessageHandler handler) { _handler = handler; }
        public HttpClient CreateClient(string name) => new HttpClient(_handler, disposeHandler: false);
    }

    // ------------------------------------------------------------------
    // 14. EnsureAvailable rejects an http source URL.
    // ------------------------------------------------------------------

    [Fact]
    public async Task EnsureAvailableRejectsNonHttpsSourceUrl()
    {
        var d = Descriptor(
            "http://example.com/model.onnx",
            8,
            new string('0', 64));
        var store = BuildStore();

        var act = async () => await store.EnsureAvailableAsync(
            d, progress: null, CancellationToken.None);

        await act.Should().ThrowAsync<ModelDownloadException>(
            "a non-loopback HTTP URL must be rejected.");
    }

    // ------------------------------------------------------------------
    // 15. EnsureAvailable rejects a non-HTTPS license URL.
    // ------------------------------------------------------------------

    [Fact]
    public async Task EnsureAvailableRejectsNonHttpsLicenseUrl()
    {
        var d = new ImageTextifyModelDescriptor(
            Id: "test-model",
            DisplayName: "Test Model",
            SourceUrl: "https://example.com/model.onnx",
            SizeBytes: 8,
            Sha256Hex: new string('0', 64),
            LicenseName: "Apache-2.0",
            LicenseUrl: "http://www.apache.org/licenses/LICENSE-2.0",
            Purpose: "Test purpose.");
        var store = BuildStore();

        var act = async () => await store.EnsureAvailableAsync(
            d, progress: null, CancellationToken.None);

        await act.Should().ThrowAsync<ModelDownloadException>(
            "a non-HTTPS license URL must be rejected.");
    }

    // ------------------------------------------------------------------
    // 16. EnsureAvailable rejects HTTP to a remote (non-loopback) host.
    // ------------------------------------------------------------------

    [Fact]
    public async Task EnsureAvailableRejectsHttpToRemoteHost()
    {
        var d = Descriptor(
            "http://example.com/model.onnx",
            8,
            new string('0', 64));
        var store = BuildStore();

        var act = async () => await store.EnsureAvailableAsync(
            d, progress: null, CancellationToken.None);

        await act.Should().ThrowAsync<ModelDownloadException>(
            "HTTP to a non-loopback host must be rejected.");
    }

    // ------------------------------------------------------------------
    // 17. EnsureAvailable rejects invalid descriptor variants.
    // ------------------------------------------------------------------

    [Fact]
    public async Task EnsureAvailableRejectsInvalidDescriptor()
    {
        var store = BuildStore();

        // Variant 1: invalid id (contains a space).
        var badId = Descriptor(
            "https://example.com/model.onnx", 8, new string('0', 64),
            id: "bad id");
        var act1 = async () => await store.EnsureAvailableAsync(
            badId, progress: null, CancellationToken.None);
        await act1.Should().ThrowAsync<ModelDownloadException>(
            "an id that does not match the regex must be rejected.");

        // Variant 2: zero size.
        var zeroSize = Descriptor(
            "https://example.com/model.onnx", 0, new string('0', 64));
        var act2 = async () => await store.EnsureAvailableAsync(
            zeroSize, progress: null, CancellationToken.None);
        await act2.Should().ThrowAsync<ModelDownloadException>(
            "a size of zero must be rejected.");

        // Variant 3: SHA-256 that is not 64 lowercase hex characters.
        var shortHash = Descriptor(
            "https://example.com/model.onnx", 8, "abc");
        var act3 = async () => await store.EnsureAvailableAsync(
            shortHash, progress: null, CancellationToken.None);
        await act3.Should().ThrowAsync<ModelDownloadException>(
            "a short hash must be rejected.");
    }

    // ------------------------------------------------------------------
    // 18. EnsureAvailable rejects path traversal in the model id.
    // ------------------------------------------------------------------

    [Fact]
    public async Task EnsureAvailableRejectsPathTraversalInId()
    {
        var d = Descriptor(
            "https://example.com/model.onnx", 8, new string('0', 64),
            id: "..evil");
        var store = BuildStore();

        var act = async () => await store.EnsureAvailableAsync(
            d, progress: null, CancellationToken.None);

        await act.Should().ThrowAsync<ModelDownloadException>(
            "path traversal in the id must be rejected.");
    }

    // ------------------------------------------------------------------
    // 19. ModelStore is registered in composition.
    // ------------------------------------------------------------------

    [Fact]
    public void ModelStoreIsRegisteredInComposition()
    {
        var store = _provider.GetService<IModelStore>();
        store.Should().NotBeNull();
        store.Should().BeOfType<ModelStore>();
    }

    // ------------------------------------------------------------------
    // 20. The named HTTP client is registered.
    // ------------------------------------------------------------------

    [Fact]
    public void ModelStoreUsesNamedHttpClientFactory()
    {
        var factory = _provider.GetRequiredService<IHttpClientFactory>();
        var act = () => factory.CreateClient("ImageTextifyModelDownload");
        act.Should().NotThrow();
    }

    // ------------------------------------------------------------------
    // 21. IFileHasher is registered in composition.
    // ------------------------------------------------------------------

    [Fact]
    public void FileHasherIsRegisteredInComposition()
    {
        var hasher = _provider.GetService<IFileHasher>();
        hasher.Should().NotBeNull();
        hasher.Should().BeOfType<Sha256FileHasher>();
    }

    // ------------------------------------------------------------------
    // 22. The manifest descriptor is valid.
    // ------------------------------------------------------------------

    [Fact]
    public void ModelManifestDescriptorIsValid()
    {
        var store = BuildStore();
        var act = () => store.GetLocalPath(ModelManifest.PaddleOcrV1);
        act.Should().NotThrow(
            "the placeholder descriptor must still pass validation.");
    }

    // ------------------------------------------------------------------
    // 23. The manifest values are placeholders.
    // ------------------------------------------------------------------

    [Fact]
    public void ModelManifestValuesArePlaceholders()
    {
        var d = ModelManifest.PaddleOcrV1;
        d.SourceUrl.Should().Contain("placeholder.invalid");
        d.Sha256Hex.Should().Be(new string('0', 64));
        d.SizeBytes.Should().Be(1L);
    }

    // ------------------------------------------------------------------
    // 24. LocalModelServer serves a deterministic fixture.
    // ------------------------------------------------------------------

    [Fact]
    public async Task LocalModelServerServesDeterministicFixture()
    {
        using var server = new LocalModelServer();
        var payload = new byte[] { 1, 2, 3 };
        server.SetPayload(payload);

        using var client = new HttpClient();
        using var response = await client.GetAsync(server.BaseUrl);
        response.EnsureSuccessStatusCode();

        var received = await response.Content.ReadAsByteArrayAsync();
        received.Should().Equal(payload);
        response.Content.Headers.ContentLength.Should().Be(3L);
    }

    private sealed class ThrowingFileHasher : IFileHasher
    {
        private readonly Exception _exception;

        public ThrowingFileHasher(Exception exception)
        {
            _exception = exception;
        }

        public Task<string> ComputeSha256Async(
            string path, CancellationToken cancellationToken)
            => throw _exception;
    }

}
