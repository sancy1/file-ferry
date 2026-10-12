// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/IAzureBlobClient.cs
// PURPOSE:  Thin wrapper interface over Azure.Storage.Blobs.BlobServiceClient.
//           Exists because BlobServiceClient is sealed and its methods are
//           not virtual, making it un-mockable with NSubstitute. This
//           interface is what AzureBlobProvider depends on; the concrete
//           AzureBlobClientAdapter wraps the real SDK. Tests substitute
//           IAzureBlobClient. No test ever touches a live Azure endpoint.
// RELATION: Implemented by AzureBlobClientAdapter. Consumed by
//           AzureBlobProvider (C.4.3) and by AzureBlobProviderTests (C.4.4).
//           Every method's XML doc states its return contract, stream
//           ownership, pagination semantics, and cancellation behavior.
// ============================================================================

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// A thin wrapper over the Azure Blob Storage SDK surface that
/// <see cref="AzureBlobProvider"/> needs. Every method documented here is
/// the entire contract between the provider and the SDK.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this interface exists.</b> The Azure SDK's
/// <c>Azure.Storage.Blobs.BlobServiceClient</c> is sealed and its methods
/// are not virtual. It cannot be substituted with NSubstitute. This
/// interface is the mockable boundary. Production code uses
/// <see cref="AzureBlobClientAdapter"/>; tests substitute this interface.
/// </para>
/// <para>
/// <b>Cancellation contract, applies to every method.</b>
/// When <paramref name="ct"/> is cancelled, the method throws
/// <see cref="OperationCanceledException"/> unchanged. The exception is
/// never wrapped, redacted, or converted to a domain exception. The
/// caller is responsible for rethrowing it upward without modification.
/// </para>
/// <para>
/// <b>Stream ownership contract.</b> Any <see cref="Stream"/> returned
/// by a method on this interface is owned by the caller. The caller is
/// responsible for disposing it. Any <see cref="Stream"/> passed as a
/// parameter is consumed but never disposed by the implementation.
/// </para>
/// </remarks>
internal interface IAzureBlobClient
{
    /// <summary>
    /// Downloads a blob and returns its content as a stream.
    /// </summary>
    /// <param name="container">Container name. Non-null, non-empty.</param>
    /// <param name="blob">
    /// Blob name. May contain slashes for nested paths — Azure Blob
    /// treats slashes as part of the blob name, not as folder separators.
    /// </param>
    /// <param name="ct">Cancellation token. See the cancellation contract in the interface remarks.</param>
    /// <returns>
    /// A stream owned by the caller. The caller must dispose it. The
    /// stream is positioned at 0 and is readable. Its length is not
    /// guaranteed to equal the blob's content length unless the SDK
    /// provides it; callers must read to end.
    /// </returns>
    /// <exception cref="Azure.RequestFailedException">
    /// Thrown when the blob does not exist (Status = 404), when the caller
    /// lacks permission (Status = 401 or 403), when the request is throttled
    /// (Status = 429), or when the service fails (Status = 5xx). The
    /// exception is not caught here; it propagates through the resilience
    /// policy, which classifies it and either retries or fails fast.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="ct"/> is cancelled. Rethrown unchanged.
    /// </exception>
    Task<Stream> DownloadAsync(string container, string blob, CancellationToken ct);

    /// <summary>
    /// Lists blobs in a container, optionally filtered by prefix.
    /// </summary>
    /// <param name="container">Container name. Non-null, non-empty.</param>
    /// <param name="prefix">
    /// Optional blob name prefix. Null or empty lists all blobs in the
    /// container.
    /// </param>
    /// <param name="continuation">
    /// Opaque continuation token from a previous call. Null on the first
    /// page. Callers must pass it back unchanged. The token's contents
    /// are defined by the Azure SDK and must not be parsed.
    /// </param>
    /// <param name="max">
    /// Maximum number of entries to return in this page. Must be positive.
    /// Implementations may return fewer than this number.
    /// </param>
    /// <param name="ct">Cancellation token. See the cancellation contract in the interface remarks.</param>
    /// <returns>
    /// An <see cref="AzureBlobPage"/> containing up to <paramref name="max"/>
    /// blob names and a continuation token. The continuation token is null
    /// when there are no more pages. Never null.
    /// </returns>
    /// <exception cref="Azure.RequestFailedException">
    /// Thrown for the same reasons as <see cref="DownloadAsync"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="ct"/> is cancelled. Rethrown unchanged.
    /// </exception>
    Task<AzureBlobPage> ListAsync(
        string container,
        string? prefix,
        string? continuation,
        int max,
        CancellationToken ct);

    /// <summary>
    /// Uploads content to a blob, overwriting any existing blob at the
    /// same name.
    /// </summary>
    /// <param name="container">Container name. Non-null, non-empty.</param>
    /// <param name="blob">Blob name. May contain slashes.</param>
    /// <param name="content">
    /// The stream to upload. Consumed from its current position to end.
    /// The implementation does not dispose this stream — the caller owns
    /// it. The stream must be readable.
    /// </param>
    /// <param name="contentType">
    /// Optional content type (for example <c>application/pdf</c>). When
    /// null, the implementation lets the SDK choose its default
    /// (<c>application/octet-stream</c>).
    /// </param>
    /// <param name="ct">Cancellation token. See the cancellation contract in the interface remarks.</param>
    /// <returns>A task that completes when the upload finishes.</returns>
    /// <exception cref="Azure.RequestFailedException">
    /// Thrown for the same reasons as <see cref="DownloadAsync"/>, plus
    /// 409 Conflict if the container does not exist.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="ct"/> is cancelled. Rethrown unchanged.
    /// </exception>
    Task UploadAsync(
        string container,
        string blob,
        Stream content,
        string? contentType,
        CancellationToken ct);

    /// <summary>
    /// Returns whether a blob exists.
    /// </summary>
    /// <param name="container">Container name. Non-null, non-empty.</param>
    /// <param name="blob">Blob name. May contain slashes.</param>
    /// <param name="ct">Cancellation token. See the cancellation contract in the interface remarks.</param>
    /// <returns>
    /// <c>true</c> when the blob exists, <c>false</c> when the blob does
    /// not exist (HTTP 404). No exception is thrown for the 404 case.
    /// </returns>
    /// <exception cref="Azure.RequestFailedException">
    /// Thrown on authentication failure (Status = 401), authorization
    /// failure (Status = 403), throttling (Status = 429), server errors
    /// (Status = 5xx), and network errors. These are not "not found"
    /// conditions and must not be silently converted to <c>false</c>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="ct"/> is cancelled. Rethrown unchanged.
    /// </exception>
    Task<bool> ExistsAsync(string container, string blob, CancellationToken ct);

    /// <summary>
    /// Deletes a blob if it exists. This is a single SDK call that is
    /// idempotent and race-free.
    /// </summary>
    /// <param name="container">Container name. Non-null, non-empty.</param>
    /// <param name="blob">Blob name. May contain slashes.</param>
    /// <param name="ct">Cancellation token. See the cancellation contract in the interface remarks.</param>
    /// <returns>
    /// <c>true</c> when a blob was deleted, <c>false</c> when no blob was
    /// present. Never throws for the missing case.
    /// </returns>
    /// <exception cref="Azure.RequestFailedException">
    /// Thrown for reasons other than 404. See <see cref="ExistsAsync"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="ct"/> is cancelled. Rethrown unchanged.
    /// </exception>
    Task<bool> DeleteIfExistsAsync(string container, string blob, CancellationToken ct);

    /// <summary>
    /// Retrieves metadata (size, last-modified, content type, ETag, and
    /// extra fields) for a blob.
    /// </summary>
    /// <param name="container">Container name. Non-null, non-empty.</param>
    /// <param name="blob">Blob name. May contain slashes.</param>
    /// <param name="ct">Cancellation token. See the cancellation contract in the interface remarks.</param>
    /// <returns>
    /// An <see cref="AzureBlobMetadata"/> describing the blob. Never null
    /// for an existing blob.
    /// </returns>
    /// <exception cref="Azure.RequestFailedException">
    /// Thrown with Status = 404 when the blob does not exist. Callers
    /// must check <see cref="ExistsAsync"/> first if the missing case
    /// must be handled without an exception. Thrown with Status = 401,
    /// 403, 429, 5xx, and on network errors for the reasons stated in
    /// <see cref="ExistsAsync"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="ct"/> is cancelled. Rethrown unchanged.
    /// </exception>
    Task<AzureBlobMetadata> GetPropertiesAsync(string container, string blob, CancellationToken ct);
}
