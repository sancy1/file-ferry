// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/S3CompatibleProvider.cs
// PURPOSE:  ICloudProvider implementation for AWS S3 and every
//           S3-compatible endpoint (Cloudflare R2, Backblaze B2,
//           Wasabi, DigitalOcean Spaces, MinIO).
// RELATION: Registered by the C.8 wiring. Dispatched by PathScheme when
//           the input is s3://bucket/key. Every SDK call runs through a
//           cloud resilience policy.
// ============================================================================

using System.Globalization;
using Amazon.S3;
using Amazon.S3.Model;
using FileHarvester.Core.Composition;
using FileHarvester.Core.Domain;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// S3-compatible cloud provider. Works with AWS S3 and any service that
/// speaks the S3 API.
/// </summary>
public sealed class S3CompatibleProvider : ICloudProvider
{
    private readonly IAmazonS3 _client;
    private readonly IResilienceExecutor _resilience;

    /// <summary>
    /// Creates the provider over an S3 client.
    /// </summary>
    /// <param name="client">The S3 client. Never null.</param>
    /// <param name="resilience">The resilience executor. Never null.</param>
    public S3CompatibleProvider(
        IAmazonS3 client,
        IResilienceExecutor resilience)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _resilience = resilience ?? throw new ArgumentNullException(nameof(resilience));
    }

    /// <inheritdoc/>
    public async Task<Stream> OpenReadAsync(
        string cloudPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cloudPath);

        var (bucket, key) = ParseS3Path(cloudPath);

        var response = await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudRead,
            operation: async token => await _client.GetObjectAsync(
                new GetObjectRequest { BucketName = bucket, Key = key },
                token).ConfigureAwait(false),
            idempotencyKey: "s3-read-" + cloudPath,
            timeBudgetMs: 30_000,
            ct: cancellationToken).ConfigureAwait(false);

        return response.ResponseStream;
    }

    /// <inheritdoc/>
    public async Task<CloudEntryPage> ListAsync(
        string prefix,
        string? continuationToken,
        int maxEntries,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        if (maxEntries <= 0) maxEntries = 1000;

        var (bucket, keyPrefix) = ParseS3Path(prefix);

        var request = new ListObjectsV2Request
        {
            BucketName = bucket,
            Prefix = keyPrefix,
            MaxKeys = maxEntries,
        };

        if (!string.IsNullOrWhiteSpace(continuationToken))
        {
            request.ContinuationToken = continuationToken;
        }

        var response = await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudSearch,
            operation: async token => await _client.ListObjectsV2Async(request, token)
                .ConfigureAwait(false),
            idempotencyKey: "s3-list-" + prefix,
            timeBudgetMs: 60_000,
            ct: cancellationToken).ConfigureAwait(false);

        var entries = new List<CloudEntry>();
        if (response.S3Objects is not null)
        {
            foreach (var obj in response.S3Objects)
            {
                var path = "s3://" + bucket + "/" + obj.Key;
                var name = CloudPath.GetFileName(path);
                entries.Add(new CloudEntry(
                    Path: path,
                    Name: name,
                    IsDirectory: false,
                    SizeBytes: obj.Size ?? 0,
                    LastModifiedUtc: obj.LastModified?.ToUniversalTime(),
                    ContentType: null));
            }
        }

        return new CloudEntryPage(entries, response.NextContinuationToken);
    }

    /// <inheritdoc/>
    public async Task<CloudWriteHandle> OpenWriteAsync(
        string cloudPath,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cloudPath);

        var (bucket, key) = ParseS3Path(cloudPath);

        // The handle accumulates bytes in memory and, on CompleteAsync,
        // transfers them to PutObjectAsync inside the cloud-write policy.
        var buffer = new MemoryStream();

        Func<CancellationToken, Task> commit = async token =>
        {
            await _resilience.ExecuteAsync(
                policyName: PolicyNames.CloudWrite,
                operation: async innerToken =>
                {
                    buffer.Position = 0;
                    var request = new PutObjectRequest
                    {
                        BucketName = bucket,
                        Key = key,
                        InputStream = buffer,
                        AutoCloseStream = false,
                    };
                    if (!string.IsNullOrWhiteSpace(contentType))
                    {
                        request.ContentType = contentType;
                    }
                    await _client.PutObjectAsync(request, innerToken)
                        .ConfigureAwait(false);
                },
                idempotencyKey: "s3-write-" + cloudPath,
                timeBudgetMs: 300_000,
                ct: token).ConfigureAwait(false);
        };

        return await Task.FromResult(new CloudWriteHandle(buffer, commit))
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(
        string cloudPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cloudPath);

        var (bucket, key) = ParseS3Path(cloudPath);

        var response = await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudRead,
            operation: async token => await _client.ListObjectsV2Async(
                new ListObjectsV2Request
                {
                    BucketName = bucket,
                    Prefix = key,
                    MaxKeys = 1,
                },
                token).ConfigureAwait(false),
            idempotencyKey: "s3-exists-" + cloudPath,
            timeBudgetMs: 30_000,
            ct: cancellationToken).ConfigureAwait(false);

        if (response.S3Objects is null || response.S3Objects.Count == 0)
        {
            return false;
        }

        // The list is by prefix; a "folder" key like "docs/" could match
        // "docs/other.txt". Confirm the exact key matches.
        foreach (var obj in response.S3Objects)
        {
            if (string.Equals(obj.Key, key, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(
        string cloudPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cloudPath);

        var (bucket, key) = ParseS3Path(cloudPath);

        await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudWrite,
            operation: async token => await _client.DeleteObjectAsync(
                new DeleteObjectRequest { BucketName = bucket, Key = key },
                token).ConfigureAwait(false),
            idempotencyKey: "s3-delete-" + cloudPath,
            timeBudgetMs: 30_000,
            ct: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ProviderMetadata> GetMetadataAsync(
        string cloudPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cloudPath);

        var (bucket, key) = ParseS3Path(cloudPath);

        var response = await _resilience.ExecuteAsync(
            policyName: PolicyNames.CloudRead,
            operation: async token => await _client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest { BucketName = bucket, Key = key },
                token).ConfigureAwait(false),
            idempotencyKey: "s3-meta-" + cloudPath,
            timeBudgetMs: 30_000,
            ct: cancellationToken).ConfigureAwait(false);

        var extra = new Dictionary<string, string>();
        if (response.Headers.ContentLength >= 0)
        {
            extra["content-length"] = response.Headers.ContentLength.ToString(CultureInfo.InvariantCulture);
        }

        return new ProviderMetadata(
            SizeBytes: response.Headers.ContentLength,
            LastModifiedUtc: response.LastModified?.ToUniversalTime(),
            ContentType: response.Headers.ContentType,
            ETag: response.ETag,
            Extra: extra);
    }

    // ------------------------------------------------------------------
    // Path parsing
    // ------------------------------------------------------------------

    private static (string Bucket, string Key) ParseS3Path(string cloudPath)
    {
        var info = PathScheme.Parse(cloudPath);
        if (info.Kind != PathSchemeKind.S3)
        {
            throw new ArgumentException(
                "Expected an s3:// path but received: " + cloudPath,
                nameof(cloudPath));
        }

        var rest = info.Rest;
        var slash = rest.IndexOf((char)47);

        if (slash < 0)
        {
            return (rest, string.Empty);
        }

        var bucket = rest.Substring(0, slash);
        var key = rest.Substring(slash + 1);
        return (bucket, key);
    }
}
