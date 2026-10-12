// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Services/ICloudContentReader.cs
// PURPOSE:  Contract for reading bytes of cloud-resolved files. Mirrors
//           IContentReader. Same non-fail-fast semantics: a single
//           unreadable file produces a placeholder, not a throw.
// RELATION: Implemented by CloudContentReader. Consumed by
//           DispatchingContentReader (C.3.3).
// ============================================================================

using FileHarvester.Core.Domain;

namespace FileHarvester.Platform.Cloud.Services;

/// <summary>
/// Reads the bytes and metadata of cloud-resolved files.
/// </summary>
/// <remarks>
/// <para>
/// The contract mirrors <see cref="IContentReader"/>. A normal I/O
/// failure (including a resilience-wrapped provider failure)
/// produces a <see cref="FileContent.Unreadable"/> placeholder, not
/// a throw. A cancelled batch throws
/// <see cref="OperationCanceledException"/> before the failed read,
/// matching the current local reader.
/// </para>
/// </remarks>
public interface ICloudContentReader
{
    /// <summary>
    /// Reads one cloud-resolved file.
    /// </summary>
    /// <param name="file">The file to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// A FileContent record. Always non-null. Check
    /// <see cref="FileContent.IsUnreadable"/> to distinguish success
    /// from a placeholder.
    /// </returns>
    Task<FileContent> ReadAsync(
        ResolvedFile file,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a batch of cloud-resolved files, reporting progress
    /// per file.
    /// </summary>
    /// <param name="files">The files to read.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="cancellationToken">Cancels the batch.</param>
    /// <returns>One FileContent per input file, in the same order.</returns>
    Task<IReadOnlyList<FileContent>> ReadManyAsync(
        IReadOnlyList<ResolvedFile> files,
        IProgress<ReadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
