// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/ICloudProvider.cs
// PURPOSE:  Storage-shaped contract for a cloud object store. Each
//           provider (S3, Azure Blob, GCS, Google Drive, OneDrive,
//           Dropbox) implements this interface. The interface is
//           provider-agnostic: it does not name a scheme, a host, a
//           bucket, or a credential shape.
// RELATION: Implemented by provider adapters in FileHarvester.Platform/Cloud.
//           Dispatched by scheme in the C.3 dispatcher. Consumed by
//           CloudFileResolver, CloudContentReader, CloudOutputSink,
//           CloudPathSearchService.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// A cloud object store: read, list, write, delete, and metadata.
/// </summary>
/// <remarks>
/// <para>
/// Every path parameter is URI-shaped (s3://bucket/key, gs://bucket/key,
/// gdrive://file/id, azure://container/blob, dropbox:///path). The
/// implementation does not verify that the path belongs to its scheme;
/// dispatch happens at the layer above.
/// </para>
/// <para>
/// Implementations must not close streams they did not open. The
/// caller owns every Stream returned by OpenReadAsync and every Stream
/// inside the handle returned by OpenWriteAsync.
/// </para>
/// <para>
/// Every operation is async and takes a cancellation token. A cancelled
/// operation is expected to throw System.OperationCanceledException.
/// </para>
/// </remarks>
public interface ICloudProvider
{
    /// <summary>
    /// Opens the object at cloudPath for reading.
    /// </summary>
    /// <param name="cloudPath">The URI-shaped object path.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// A readable Stream positioned at the start. The caller closes it.
    /// The provider does not.
    /// </returns>
    Task<Stream> OpenReadAsync(
        string cloudPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists entries under the given prefix. Paginated.
    /// </summary>
    /// <param name="prefix">The URI-shaped prefix. May be empty.</param>
    /// <param name="continuationToken">
    /// The opaque token from a prior page, or null to start from the
    /// beginning.
    /// </param>
    /// <param name="maxEntries">
    /// Upper bound on entries returned in this page.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// One page of entries plus an optional continuation token.
    /// </returns>
    Task<CloudEntryPage> ListAsync(
        string prefix,
        string? continuationToken,
        int maxEntries,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the object at cloudPath for writing.
    /// </summary>
    /// <param name="cloudPath">The URI-shaped object path.</param>
    /// <param name="contentType">
    /// Optional MIME type. Null means the provider decides.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// A CloudWriteHandle. The caller writes to the handle stream, then
    /// calls CompleteAsync to commit, then disposes the handle.
    /// </returns>
    Task<CloudWriteHandle> OpenWriteAsync(
        string cloudPath,
        string? contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True if the object at cloudPath exists.
    /// </summary>
    /// <param name="cloudPath">The URI-shaped object path.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>True when the object exists.</returns>
    Task<bool> ExistsAsync(
        string cloudPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the object at cloudPath. No-op when it does not exist.
    /// </summary>
    /// <param name="cloudPath">The URI-shaped object path.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task DeleteAsync(
        string cloudPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns metadata for the object at cloudPath.
    /// </summary>
    /// <param name="cloudPath">The URI-shaped object path.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A ProviderMetadata. Throws when the object does not exist.</returns>
    Task<ProviderMetadata> GetMetadataAsync(
        string cloudPath,
        CancellationToken cancellationToken = default);
}
