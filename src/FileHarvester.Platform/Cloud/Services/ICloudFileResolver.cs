// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Services/ICloudFileResolver.cs
// PURPOSE:  Contract for resolving cloud-shaped FileRequests. Mirrors
//           IFileResolver for cloud paths. The dispatcher routes
//           cloud requests here; local requests go to FileResolver.
// RELATION: Implemented by CloudFileResolver. Consumed by
//           DispatchingFileResolver (C.3.3).
// ============================================================================

using FileHarvester.Core.Domain;

namespace FileHarvester.Platform.Cloud.Services;

/// <summary>
/// Resolves cloud-shaped FileRequests to ResolvedFile records.
/// </summary>
/// <remarks>
/// <para>
/// Local requests are ignored by implementations of this interface.
/// The dispatcher routes them to <see cref="IFileResolver"/> instead.
/// </para>
/// <para>
/// A request whose scheme has no registered provider produces no
/// <see cref="ResolvedFile"/>. The implementation may report the
/// unsupported scheme in the progress sink; it does not throw.
/// </para>
/// </remarks>
public interface ICloudFileResolver
{
    /// <summary>
    /// Resolves a batch of cloud-shaped requests.
    /// </summary>
    /// <param name="requests">The requests, in original order.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="cancellationToken">Cancels the batch.</param>
    /// <returns>
    /// Zero or more ResolvedFile records. Ordering follows the input
    /// where possible.
    /// </returns>
    Task<IReadOnlyList<ResolvedFile>> ResolveAsync(
        IReadOnlyList<FileRequest> requests,
        IProgress<ResolveProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
