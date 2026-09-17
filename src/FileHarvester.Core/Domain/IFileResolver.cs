// ============================================================================
// FILE:     src\FileHarvester.Core/Domain/IFileResolver.cs
// PURPOSE:  Contract for the service that turns a batch of FileRequest
//           records into ResolvedFile records by locating each on disk.
// RELATION: Implemented by FileResolver in Phase 1.1. Consumed by the
//           Resolved Files panel (Phase 3.2). Uses "file-read" and
//           "path-search" resilience policies.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// Locates files on disk. The first stage of the harvest pipeline.
/// </summary>
public interface IFileResolver
{
    /// <summary>
    /// Resolves a batch of FileRequest records using default search
    /// options (exact filename match).
    /// </summary>
    Task<IReadOnlyList<ResolvedFile>> ResolveAsync(
        IReadOnlyList<FileRequest> requests,
        IProgress<ResolveProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves with explicit search options. Use <see cref="SearchMode.BaseName"/>
    /// to expand a bare name like "note" into every matching file
    /// regardless of extension.
    /// </summary>
    /// <param name="requests">The user's input, in original order.</param>
    /// <param name="searchOptions">
    /// Mode, result cap, timeout, and skip list for any PC-wide search
    /// performed as part of resolution.
    /// </param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="cancellationToken">Cancels the batch.</param>
    /// <returns>
    /// Zero or more ResolvedFile records per request. Ordering follows the
    /// input where possible.
    /// </returns>
    Task<IReadOnlyList<ResolvedFile>> ResolveAsync(
        IReadOnlyList<FileRequest> requests,
        SearchOptions searchOptions,
        IProgress<ResolveProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One progress tick from the resolver.
/// </summary>
public sealed record ResolveProgress(
    int Index,
    int Total,
    int Processed,
    FileStatus Status,
    string? ResolvedPath = null);
