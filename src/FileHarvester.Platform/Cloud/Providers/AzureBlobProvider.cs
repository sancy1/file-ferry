// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/AzureBlobProvider.cs
// PURPOSE:  ICloudProvider implementation for Azure Blob Storage. Second
//           member of the API-key family (after S3). Mirrors
//           S3CompatibleProvider one-for-one: same constructor shape, same
//           resilience wrapping with named arguments, same no-fallback
//           policy (ResilienceException propagates), same cancellation
//           contract (OperationCanceledException rethrown unchanged).
// RELATION: Implements ICloudProvider. Constructed in C.8's DI wiring.
//           Consumes IAzureBlobClient (the mockable wrapper over
//           BlobServiceClient) and IResilienceExecutor. Uses
//           PolicyNames.CloudRead, CloudWrite, CloudSearch.
//           Internal because IAzureBlobClient is internal; the public
//           contract is ICloudProvider in FileHarvester.Core.
// ============================================================================

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Core.Domain;
using FileHarvester.Core.Composition;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// Azure Blob Storage implementation of <see cref="ICloudProvider"/>.
/// </summary>
internal sealed class AzureBlobProvider : ICloudProvider
{
    private readonly IAzureBlobClient _client;
    private readonly IResilienceExecutor _resilience;

    /// <summary>
    /// Creates a provider over the given wrapper client.
    /// </summary>
    /// <param name="client">The wrapper over BlobServiceClient. Not null.</param>
    /// <param name="resilience">The resilience executor. Not null.</param>
    public AzureBlobProvider(
        IAzureBlobClient client,
        IResilienceExecutor resilience)
    {
        _client     = client     ?? throw new ArgumentNullException(nameof(client));
        _resilience = resilience ?? throw new ArgumentNullException(nameof(resilience));
    }

    // ------------------------------------------------------------------
    // ICloudProvider
    // ------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<Stream> OpenReadAsync(
        string cloudPath,
        CancellationToken ct = default)
    {
        var (container, blob) = ParseAzurePath(cloudPath);

        return await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudRead,
            operation: async token => await _client
                .DownloadAsync(container, blob, token)
                .ConfigureAwait(false),
            idempotencyKey: "azure-read-" + cloudPath,
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
        var (container, blobPrefix) = ParseAzurePath(prefix);

        var page = await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudSearch,
            operation: async token => await _client
                .ListAsync(container, blobPrefix, continuationToken, maxEntries, token)
                .ConfigureAwait(false),
            idempotencyKey: "azure-list-" + prefix,
            timeBudgetMs: 60_000,
            ct: ct).ConfigureAwait(false);

        // The Azure wrapper returns only blob names, no size or timestamp.
        // SizeBytes is 0 and LastModifiedUtc is null; callers that need
        // those values call GetMetadataAsync. This mirrors S3's listing
        // shape, which surfaces whatever the SDK provides and no more.
        var entries = new List<CloudEntry>(page.BlobNames.Count);
        foreach (var name in page.BlobNames)
        {
            var path = "azure://" + container + "/" + name;
            var entryName = GetLastSegment(name);
            entries.Add(new CloudEntry(
                Path: path,
                Name: entryName,
                IsDirectory: false,
                SizeBytes: 0,
                LastModifiedUtc: null,
                ContentType: null));
        }

        return new CloudEntryPage(entries, page.ContinuationToken);
    }

    /// <inheritdoc />
    public Task<CloudWriteHandle> OpenWriteAsync(
        string cloudPath,
        string? contentType,
        CancellationToken ct = default)
    {
        var (container, blob) = ParseAzurePath(cloudPath);

        // The handle accumulates bytes in memory and, on CompleteAsync,
        // uploads them through the cloud-write policy. Mirrors S3.
        var buffer = new MemoryStream();

        Func<CancellationToken, Task> complete = async token =>
        {
            buffer.Position = 0;

            await _resilience.ExecuteAsync(
                policyName: PolicyNames.CloudWrite,
                operation: async innerToken => await _client
                    .UploadAsync(container, blob, buffer, contentType, innerToken)
                    .ConfigureAwait(false),
                idempotencyKey: "azure-write-" + cloudPath,
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
        var (container, blob) = ParseAzurePath(cloudPath);

        return await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudRead,
            operation: async token => await _client
                .ExistsAsync(container, blob, token)
                .ConfigureAwait(false),
            idempotencyKey: "azure-exists-" + cloudPath,
            timeBudgetMs: 30_000,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        string cloudPath,
        CancellationToken ct = default)
    {
        var (container, blob) = ParseAzurePath(cloudPath);

        await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudWrite,
            operation: async token => await _client
                .DeleteIfExistsAsync(container, blob, token)
                .ConfigureAwait(false),
            idempotencyKey: "azure-delete-" + cloudPath,
            timeBudgetMs: 30_000,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ProviderMetadata> GetMetadataAsync(
        string cloudPath,
        CancellationToken ct = default)
    {
        var (container, blob) = ParseAzurePath(cloudPath);

        var meta = await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudRead,
            operation: async token => await _client
                .GetPropertiesAsync(container, blob, token)
                .ConfigureAwait(false),
            idempotencyKey: "azure-meta-" + cloudPath,
            timeBudgetMs: 30_000,
            ct: ct).ConfigureAwait(false);

        return new ProviderMetadata(
            SizeBytes:       meta.SizeBytes,
            LastModifiedUtc: meta.LastModifiedUtc,
            ContentType:     meta.ContentType,
            ETag:            meta.ETag,
            Extra:           meta.Extra);
    }

    // ------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns the last segment of a slash-separated name. Azure treats
    /// slashes as part of the blob name, not as folder separators, so
    /// the last segment is the file-like portion.
    /// </summary>
    private static string GetLastSegment(string name)
    {
        var slash = name.LastIndexOf((char)47);
        return slash < 0 ? name : name.Substring(slash + 1);
    }

    /// <summary>
    /// Parses an <c>azure://container/blob</c> URI into a container name
    /// and a blob name (which may itself contain slashes). Mirrors the
    /// <c>ParseS3Path</c> helper on the S3 provider.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The path does not start with <c>azure://</c>, or the container
    /// segment is empty.
    /// </exception>
    private static (string Container, string Blob) ParseAzurePath(string cloudPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cloudPath);

        const string prefix = "azure://";
        if (!cloudPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Path does not start with azure://",
                nameof(cloudPath));
        }

        var rest = cloudPath.Substring(prefix.Length);
        var slash = rest.IndexOf((char)47);

        if (slash < 0)
        {
            if (rest.Length == 0)
            {
                throw new ArgumentException(
                    "azure:// path has no container name.",
                    nameof(cloudPath));
            }

            return (rest, string.Empty);
        }

        var container = rest.Substring(0, slash);
        var blob      = rest.Substring(slash + 1);

        if (container.Length == 0)
        {
            throw new ArgumentException(
                "azure:// path has no container name.",
                nameof(cloudPath));
        }

        return (container, blob);
    }
}
