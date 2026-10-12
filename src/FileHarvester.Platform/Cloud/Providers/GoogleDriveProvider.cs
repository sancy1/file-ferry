// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/GoogleDriveProvider.cs
// PURPOSE:  ICloudProvider implementation for Google Drive. The first
//           OAuth-family provider. Mirrors GcsObjectProvider and
//           AzureBlobProvider in shape: same constructor, same resilience
//           wrapping with named arguments, same no-fallback policy. Drive
//           has no bucket or key: files are identified by id in a
//           gdrive://<id> URI. The optional [instance] segment in
//           gdrive://[instance]/<id> is stripped by PathScheme.Parse.
//           OpenReadAsync is metadata-first: a Google-native document
//           throws GoogleNativeFileRequiresExportException instead of
//           downloading.
// RELATION: Implements ICloudProvider. Consumes IGDriveClient and
//           IResilienceExecutor. Constructed in C.8's DI wiring.
//           Internal because IGDriveClient is internal.
// ============================================================================

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Core.Composition;
using FileHarvester.Core.Domain;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// Google Drive implementation of <see cref="ICloudProvider"/>.
/// </summary>
internal sealed class GoogleDriveProvider : ICloudProvider
{
    private const string GDriveScheme = "gdrive://";
    private const string GoogleAppsMimePrefix = "application/vnd.google-apps.";
    private const string FolderMimeType = "application/vnd.google-apps.folder";

    private readonly IGDriveClient _client;
    private readonly IResilienceExecutor _resilience;

    /// <summary>
    /// Creates a provider over the Drive wrapper.
    /// </summary>
    /// <param name="client">The wrapper over the Drive SDK. Not null.</param>
    /// <param name="resilience">The resilience executor. Not null.</param>
    public GoogleDriveProvider(
        IGDriveClient client,
        IResilienceExecutor resilience)
    {
        _client     = client     ?? throw new ArgumentNullException(nameof(client));
        _resilience = resilience ?? throw new ArgumentNullException(nameof(resilience));
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadAsync(
        string cloudPath,
        CancellationToken ct = default)
    {
        var fileId = ParseGDrivePath(cloudPath);

        // Metadata-first: discover the MIME type before downloading. A
        // Google-native document has no downloadable content; it must be
        // exported. The provider does not download before knowing this.
        var meta = await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudRead,
            operation: async token => await _client
                .GetPropertiesAsync(fileId, token)
                .ConfigureAwait(false),
            idempotencyKey: "drive-meta-" + fileId,
            timeBudgetMs: 30_000,
            ct: ct).ConfigureAwait(false);

        if (meta.MimeType is not null
            && meta.MimeType.StartsWith(GoogleAppsMimePrefix, StringComparison.Ordinal))
        {
            throw new GoogleNativeFileRequiresExportException(fileId);
        }

        return await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudRead,
            operation: async token => await _client
                .DownloadAsync(fileId, token)
                .ConfigureAwait(false),
            idempotencyKey: "drive-read-" + fileId,
            timeBudgetMs: 30_000,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CloudEntryPage> ListAsync(
        string prefix,
        string? continuationToken,
        int maxEntries,
        CancellationToken ct = default)
    {
        var folderId = ParseGDrivePath(prefix);

        var page = await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudSearch,
            operation: async token => await _client
                .ListAsync(folderId, continuationToken, maxEntries, token)
                .ConfigureAwait(false),
            idempotencyKey: "drive-list-" + folderId,
            timeBudgetMs: 60_000,
            ct: ct).ConfigureAwait(false);

        var entries = new List<CloudEntry>(page.Items.Count);
        foreach (var item in page.Items)
        {
            var isFolder = string.Equals(item.MimeType, FolderMimeType, StringComparison.Ordinal);
            entries.Add(new CloudEntry(
                Path: GDriveScheme + item.Id,
                Name: item.Name ?? item.Id,
                IsDirectory: isFolder,
                SizeBytes: item.SizeBytes,
                LastModifiedUtc: item.LastModifiedUtc,
                ContentType: item.MimeType));
        }

        return new CloudEntryPage(entries, page.ContinuationToken);
    }

    /// <inheritdoc />
    public Task<CloudWriteHandle> OpenWriteAsync(
        string cloudPath,
        string? contentType,
        CancellationToken ct = default)
    {
        var fileId = ParseGDrivePath(cloudPath);

        // The handle accumulates bytes in memory and, on CompleteAsync,
        // uploads them through the cloud-write policy. Mirrors S3, Azure,
        // and GCS.
        var buffer = new MemoryStream();

        Func<CancellationToken, Task> complete = async token =>
        {
            buffer.Position = 0;

            await _resilience.ExecuteAsync(
                policyName: PolicyNames.CloudWrite,
                operation: async innerToken => await _client
                    .UpdateAsync(fileId, contentType, buffer, innerToken)
                    .ConfigureAwait(false),
                idempotencyKey: "drive-write-" + fileId,
                timeBudgetMs: 300_000,
                ct: token).ConfigureAwait(false);
        };

        return Task.FromResult(new CloudWriteHandle(buffer, complete));
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        string cloudPath,
        CancellationToken ct = default)
    {
        var fileId = ParseGDrivePath(cloudPath);

        return await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudRead,
            operation: async token => await _client
                .ExistsAsync(fileId, token)
                .ConfigureAwait(false),
            idempotencyKey: "drive-exists-" + fileId,
            timeBudgetMs: 30_000,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        string cloudPath,
        CancellationToken ct = default)
    {
        var fileId = ParseGDrivePath(cloudPath);

        await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudWrite,
            operation: async token => await _client
                .DeleteIfExistsAsync(fileId, token)
                .ConfigureAwait(false),
            idempotencyKey: "drive-delete-" + fileId,
            timeBudgetMs: 30_000,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ProviderMetadata> GetMetadataAsync(
        string cloudPath,
        CancellationToken ct = default)
    {
        var fileId = ParseGDrivePath(cloudPath);

        var meta = await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudRead,
            operation: async token => await _client
                .GetPropertiesAsync(fileId, token)
                .ConfigureAwait(false),
            idempotencyKey: "drive-meta-" + fileId,
            timeBudgetMs: 30_000,
            ct: ct).ConfigureAwait(false);

        var extra = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["md5-checksum"] = meta.Md5Checksum ?? string.Empty,
            ["trashed"] = meta.IsTrashed ? "true" : "false",
        };

        return new ProviderMetadata(
            SizeBytes: meta.SizeBytes,
            LastModifiedUtc: meta.LastModifiedUtc,
            ContentType: meta.MimeType,
            ETag: null,
            Extra: extra);
    }

    // ------------------------------------------------------------------
    // Path parsing.
    // ------------------------------------------------------------------

    /// <summary>
    /// Parses a gdrive://&lt;file-id&gt; URI into the file id. Uses
    /// PathScheme.Parse so the optional [instance] segment is stripped
    /// correctly. Drive has no bucket or key: the whole remainder after
    /// the scheme is the id.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The path does not start with gdrive://, or the file id is empty.
    /// </exception>
    private static string ParseGDrivePath(string cloudPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cloudPath);

        var info = PathScheme.Parse(cloudPath);

        if (info.Kind != PathSchemeKind.GoogleDrive)
        {
            throw new ArgumentException(
                "Path does not start with gdrive://",
                nameof(cloudPath));
        }

        if (string.IsNullOrEmpty(info.Rest))
        {
            throw new ArgumentException(
                "gdrive:// path has no file id.",
                nameof(cloudPath));
        }

        return info.Rest;
    }
}
