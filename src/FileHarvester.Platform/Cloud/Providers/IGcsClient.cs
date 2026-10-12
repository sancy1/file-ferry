// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/IGcsClient.cs
// PURPOSE:  Thin wrapper interface over Google.Cloud.Storage.V1.StorageClient.
//           Exists because StorageClient is a concrete, factory-created
//           abstraction whose base convenience methods are unsafe to
//           substitute directly. This interface is what GcsObjectProvider
//           depends on; the concrete GcsClientAdapter wraps the real SDK.
//           Tests substitute IGcsClient. No test ever touches a live GCS
//           endpoint.
// RELATION: Implemented by GcsClientAdapter. Consumed by GcsObjectProvider
//           (C.5.3) and by GcsObjectProviderTests (C.5.5). Every method's
//           XML doc states its return contract, stream ownership,
//           pagination semantics, and cancellation behavior.
// ============================================================================

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// A thin wrapper over the Google Cloud Storage SDK surface that
/// <see cref="GcsObjectProvider"/> needs. Every method documented here is
/// the entire contract between the provider and the SDK.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this interface exists.</b> The GCS SDK's
/// <c>Google.Cloud.Storage.V1.StorageClient</c> is a concrete class whose
/// convenience methods are implemented on an internal subclass and throw
/// NotImplementedException when not overridden. It cannot be safely
/// substituted with NSubstitute. This interface is the mockable boundary.
/// Production code uses <see cref="GcsClientAdapter"/>; tests substitute
/// this interface.
/// </para>
/// <para>
/// <b>Cancellation contract, applies to every method.</b>
/// When <paramref name="ct"/> is cancelled, the method throws
/// <see cref="OperationCanceledException"/> unchanged. The exception is
/// never wrapped, redacted, or converted to a domain exception.
/// </para>
/// <para>
/// <b>Stream ownership contract.</b> Any <see cref="Stream"/> returned
/// by a method on this interface is owned by the caller. Any
/// <see cref="Stream"/> passed as a parameter is consumed but never
/// disposed by the implementation.
/// </para>
/// <para>
/// <b>Not-found contract.</b> <see cref="ExistsAsync"/> and
/// <see cref="DeleteIfExistsAsync"/> catch the SDK's not-found exception
/// inside the adapter and convert it to a false result. Every other
/// failure — authentication, authorization, throttling, network, server —
/// propagates unchanged. This is deliberate: the resilience pipeline must
/// never see a genuine not-found as a retryable failure.
/// </para>
/// </remarks>
internal interface IGcsClient
{
    /// <summary>
    /// Downloads an object and returns its content as a stream.
    /// </summary>
    /// <param name="bucket">Bucket name. Non-null, non-empty.</param>
    /// <param name="objectName">
    /// Object name. May contain slashes for nested paths — GCS treats
    /// slashes as part of the object name, not as folder separators.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A stream owned by the caller. The caller must dispose it. The
    /// stream is positioned at 0 and is readable.
    /// </returns>
    /// <exception cref="Google.GoogleApiException">
    /// Thrown when the object does not exist (Status = 404), when the
    /// caller lacks permission (Status = 401 or 403), when the request
    /// is throttled (Status = 429), or when the service fails (Status
    /// = 5xx). The exception is not caught here; it propagates through
    /// the resilience policy.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="ct"/> is cancelled. Rethrown unchanged.
    /// </exception>
    Task<Stream> DownloadAsync(string bucket, string objectName, CancellationToken ct);

    /// <summary>
    /// Lists objects in a bucket, optionally filtered by prefix.
    /// </summary>
    /// <param name="bucket">Bucket name. Non-null, non-empty.</param>
    /// <param name="prefix">
    /// Optional object name prefix. Null or empty lists all objects in
    /// the bucket.
    /// </param>
    /// <param name="continuation">
    /// Opaque continuation token from a previous call. Null on the first
    /// page. Callers must pass it back unchanged. The token is the GCS
    /// NextPageToken and must not be parsed.
    /// </param>
    /// <param name="max">
    /// Maximum number of entries to return in this page. Must be positive.
    /// Implementations may return fewer than this number.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="GcsObjectPage"/> containing up to <paramref name="max"/>
    /// object names and a continuation token. The continuation token is
    /// null when there are no more pages. Never null.
    /// </returns>
    /// <exception cref="Google.GoogleApiException">
    /// Thrown for the same reasons as <see cref="DownloadAsync"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="ct"/> is cancelled. Rethrown unchanged.
    /// </exception>
    Task<GcsObjectPage> ListAsync(
        string bucket,
        string prefix,
        string? continuation,
        int max,
        CancellationToken ct);

    /// <summary>
    /// Uploads content to an object, overwriting any existing object at
    /// the same name.
    /// </summary>
    /// <param name="bucket">Bucket name. Non-null, non-empty.</param>
    /// <param name="objectName">Object name. May contain slashes.</param>
    /// <param name="contentType">
    /// Optional content type. When null, the implementation lets the SDK
    /// choose its default.
    /// </param>
    /// <param name="content">
    /// The stream to upload. Consumed from its current position to end.
    /// The implementation does not dispose this stream — the caller owns
    /// it. The stream must be readable.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the upload finishes.</returns>
    /// <exception cref="Google.GoogleApiException">
    /// Thrown for the same reasons as <see cref="DownloadAsync"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="ct"/> is cancelled. Rethrown unchanged.
    /// </exception>
    Task UploadAsync(
        string bucket,
        string objectName,
        string? contentType,
        Stream content,
        CancellationToken ct);

    /// <summary>
    /// Returns whether an object exists. This is a metadata probe; it
    /// does not download the object's content.
    /// </summary>
    /// <param name="bucket">Bucket name. Non-null, non-empty.</param>
    /// <param name="objectName">Object name. May contain slashes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> when the object exists, <c>false</c> when the object
    /// does not exist (HTTP 404). No exception is thrown for the 404 case.
    /// </returns>
    /// <exception cref="Google.GoogleApiException">
    /// Thrown on authentication failure (Status = 401), authorization
    /// failure (Status = 403), throttling (Status = 429), server errors
    /// (Status = 5xx), and network errors. These are not "not found"
    /// conditions and must not be silently converted to <c>false</c>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="ct"/> is cancelled. Rethrown unchanged.
    /// </exception>
    Task<bool> ExistsAsync(string bucket, string objectName, CancellationToken ct);

    /// <summary>
    /// Deletes an object if it exists. This is idempotent and race-free.
    /// </summary>
    /// <param name="bucket">Bucket name. Non-null, non-empty.</param>
    /// <param name="objectName">Object name. May contain slashes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> when an object was deleted, <c>false</c> when no object
    /// was present. Never throws for the missing case.
    /// </returns>
    /// <exception cref="Google.GoogleApiException">
    /// Thrown for reasons other than 404. See <see cref="ExistsAsync"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="ct"/> is cancelled. Rethrown unchanged.
    /// </exception>
    Task<bool> DeleteIfExistsAsync(string bucket, string objectName, CancellationToken ct);

    /// <summary>
    /// Retrieves metadata (size, last-modified, content type, ETag, and
    /// extra fields) for an object. This is a metadata call; it does not
    /// download the object's content.
    /// </summary>
    /// <param name="bucket">Bucket name. Non-null, non-empty.</param>
    /// <param name="objectName">Object name. May contain slashes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="GcsObjectMetadata"/> describing the object. Never
    /// null for an existing object.
    /// </returns>
    /// <exception cref="Google.GoogleApiException">
    /// Thrown with Status = 404 when the object does not exist. Callers
    /// must check <see cref="ExistsAsync"/> first if the missing case
    /// must be handled without an exception. Thrown with Status = 401,
    /// 403, 429, 5xx, and on network errors for the reasons stated in
    /// <see cref="ExistsAsync"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="ct"/> is cancelled. Rethrown unchanged.
    /// </exception>
    Task<GcsObjectMetadata> GetPropertiesAsync(string bucket, string objectName, CancellationToken ct);
}
