// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/IContentReader.cs
// PURPOSE:  Contract for the service that reads file bytes and metadata
//           for every ResolvedFile, producing FileContent records.
// RELATION: Implemented by ContentReader in Phase 1.2. Consumed by the
//           JobRunner (Phase 3.4) as the second stage of the harvest
//           pipeline, after IFileResolver and before IBundleComposer.
//           Every read runs through the Portfolio.Resilience "file-read"
//           policy.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// Reads the bytes and metadata of resolved files.
/// </summary>
public interface IContentReader
{
    /// <summary>
    /// Reads one file. Never throws for a normal I/O failure — instead
    /// returns a FileContent.Unreadable placeholder with the classified
    /// reason, so a single bad file cannot abort a large batch.
    /// </summary>
    /// <param name="file">The file to read.</param>
    /// <param name="cancellationToken">
    /// Cancels the read. On cancellation, the operation returns an
    /// unreadable placeholder rather than throwing, matching the
    /// non-fail-fast contract.
    /// </param>
    /// <returns>
    /// A FileContent record. Always non-null. Check
    /// <see cref="FileContent.IsUnreadable"/> to distinguish success from
    /// a placeholder.
    /// </returns>
    Task<FileContent> ReadAsync(
        ResolvedFile file,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a batch of files, reporting progress per file. Same
    /// non-fail-fast semantics as the single-file overload: every file
    /// produces exactly one FileContent, successful or placeholder.
    /// </summary>
    /// <param name="files">The files to read.</param>
    /// <param name="progress">
    /// Optional progress sink. Reports one ReadProgress per file as it
    /// completes.
    /// </param>
    /// <param name="cancellationToken">Cancels the batch.</param>
    /// <returns>
    /// One FileContent per input file, in the same order as the input.
    /// </returns>
    Task<IReadOnlyList<FileContent>> ReadManyAsync(
        IReadOnlyList<ResolvedFile> files,
        IProgress<ReadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One progress tick from the content reader.
/// </summary>
/// <param name="Index">
/// The zero-based position within the batch, matching the order of the
/// input list.
/// </param>
/// <param name="Total">Total number of files in this batch.</param>
/// <param name="Processed">
/// How many files have been read so far, including this one.
/// </param>
/// <param name="BytesRead">
/// Cumulative bytes read across the batch so far.
/// </param>
/// <param name="Status">
/// The result of reading this file: Found (success), ReadError
/// (placeholder), or Skipped (binary content or size limit).
/// </param>
public sealed record ReadProgress(
    int Index,
    int Total,
    int Processed,
    long BytesRead,
    FileStatus Status);
