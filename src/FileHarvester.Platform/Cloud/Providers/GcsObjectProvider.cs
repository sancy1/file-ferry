// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/GcsObjectProvider.cs
// PURPOSE:  ICloudProvider implementation for Google Cloud Storage. Third
//           member of the API-key family (after S3 and Azure). Mirrors
//           S3CompatibleProvider and AzureBlobProvider one-for-one: same
//           constructor shape, same resilience wrapping with named
//           arguments, same no-fallback policy (ResilienceException
//           propagates), same cancellation contract.
// RELATION: Implements ICloudProvider. Constructed in C.8's DI wiring.
//           Consumes IGcsClient (the mockable wrapper over StorageClient)
//           and IResilienceExecutor. Uses PolicyNames.CloudRead,
//           CloudWrite, CloudSearch. Internal because IGcsClient is
//           internal; the public contract is ICloudProvider in Core.
// ============================================================================

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Core.Composition;
using FileHarvester.Core.Domain;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// Google Cloud Storage implementation of <see cref="ICloudProvider"/>.
/// </summary>
internal sealed class GcsObjectProvider : ICloudProvider
{
    private readonly IGcsClient _client;
    private readonly IResilienceExecutor _resilience;

    /// <summary>
    /// Creates a provider over the given wrapper client.
    /// </summary>
    /// <param name="client">The wrapper over StorageClient. Not null.</param>
    /// <param name="resilience">The resilience executor. Not null.</param>
    public GcsObjectProvider(
        IGcsClient client,
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
        var (bucket, objectName) = ParseGcsPath(cloudPath);

        return await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudRead,
            operation: async token => await _client
                .DownloadAsync(bucket, objectName, token)
                .ConfigureAwait(false),
            idempotencyKey: "gcs-read-" + cloudPath,
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
        var (bucket, objectPrefix) = ParseGcsPath(prefix);

        var page = await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudSearch,
            operation: async token => await _client
                .ListAsync(bucket, objectPrefix, continuationToken, maxEntries, token)
                .ConfigureAwait(false),
            idempotencyKey: "gcs-list-" + prefix,
            timeBudgetMs: 60_000,
            ct: ct).ConfigureAwait(false);

        // The GCS wrapper returns only object names, no size or timestamp.
        // SizeBytes is 0 and LastModifiedUtc is null; callers that need
        // those values call GetMetadataAsync. This mirrors Azure's
        // listing shape.
        var entries = new List<CloudEntry>(page.ObjectNames.Count);
        foreach (var name in page.ObjectNames)
        {
            var path = "gs://" + bucket + "/" + name;
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
        var (bucket, objectName) = ParseGcsPath(cloudPath);

        // The handle accumulates bytes in memory and, on CompleteAsync,
        // uploads them through the cloud-write policy. Mirrors S3 and
        // Azure.
        var buffer = new MemoryStream();

        Func<CancellationToken, Task> complete = async token =>
        {
            buffer.Position = 0;

            await _resilience.ExecuteAsync(
                policyName: PolicyNames.CloudWrite,
                operation: async innerToken => await _client
                    .UploadAsync(bucket, objectName, contentType, buffer, innerToken)
                    .ConfigureAwait(false),
                idempotencyKey: "gcs-write-" + cloudPath,
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
        var (bucket, objectName) = ParseGcsPath(cloudPath);

        return await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudRead,
            operation: async token => await _client
                .ExistsAsync(bucket, objectName, token)
                .ConfigureAwait(false),
            idempotencyKey: "gcs-exists-" + cloudPath,
            timeBudgetMs: 30_000,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        string cloudPath,
        CancellationToken ct = default)
    {
        var (bucket, objectName) = ParseGcsPath(cloudPath);

        await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudWrite,
            operation: async token => await _client
                .DeleteIfExistsAsync(bucket, objectName, token)
                .ConfigureAwait(false),
            idempotencyKey: "gcs-delete-" + cloudPath,
            timeBudgetMs: 30_000,
            ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ProviderMetadata> GetMetadataAsync(
        string cloudPath,
        CancellationToken ct = default)
    {
        var (bucket, objectName) = ParseGcsPath(cloudPath);

        var meta = await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudRead,
            operation: async token => await _client
                .GetPropertiesAsync(bucket, objectName, token)
                .ConfigureAwait(false),
            idempotencyKey: "gcs-meta-" + cloudPath,
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
    /// Returns the last segment of a slash-separated name. GCS treats
    /// slashes as part of the object name, not as folder separators, so
    /// the last segment is the file-like portion.
    /// </summary>
    private static string GetLastSegment(string name)
    {
        var slash = name.LastIndexOf((char)47);
        return slash < 0 ? name : name.Substring(slash + 1);
    }

    /// <summary>
    /// Parses a <c>gs://bucket/object</c> URI into a bucket name and an
    /// object name (which may itself contain slashes). Mirrors the
    /// <c>ParseAzurePath</c> helper on the Azure provider.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The path does not start with <c>gs://</c>, or the bucket segment
    /// is empty.
    /// </exception>
    private static (string Bucket, string ObjectName) ParseGcsPath(string cloudPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cloudPath);

        const string prefix = "gs://";
        if (!cloudPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Path does not start with gs://",
                nameof(cloudPath));
        }

        var rest = cloudPath.Substring(prefix.Length);
        var slash = rest.IndexOf((char)47);

        if (slash < 0)
        {
            if (rest.Length == 0)
            {
                throw new ArgumentException(
                    "gs:// path has no bucket name.",
                    nameof(cloudPath));
            }

            return (rest, string.Empty);
        }

        var bucket = rest.Substring(0, slash);
        var objectName = rest.Substring(slash + 1);

        if (bucket.Length == 0)
        {
            throw new ArgumentException(
                "gs:// path has no bucket name.",
                nameof(cloudPath));
        }

        return (bucket, objectName);
    }
}
