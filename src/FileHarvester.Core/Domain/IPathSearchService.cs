// ============================================================================
// FILE:     src\FileHarvester.Core/Domain/IPathSearchService.cs
// PURPOSE:  Contract for whole-PC filename search. Used by the resolver
//           when a bare filename is not found in any standard location.
// RELATION: Implemented by WindowsPathSearch (Phase 5.0) and, in Phase 5.1,
//           by MacPathSearch and LinuxPathSearch. Consumed by FileResolver.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// Searches the entire machine for files matching a filename.
/// </summary>
public interface IPathSearchService
{
    /// <summary>
    /// Finds every file on the machine whose name matches the query under
    /// the given options.
    /// </summary>
    /// <param name="fileName">
    /// The filename or base name to search for. Interpretation depends on
    /// <see cref="SearchOptions.Mode"/>.
    /// </param>
    /// <param name="options">
    /// Search configuration: mode, max results, timeout, skip list.
    /// </param>
    /// <param name="progress">
    /// Optional progress sink. Reports per-tier progress so the UI can
    /// show activity during a long search.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the search. Implementations must honour this promptly.
    /// </param>
    /// <returns>
    /// Zero or more absolute paths to matching files. Empty when no match
    /// was found. Ordering is implementation-defined.
    /// </returns>
    Task<IReadOnlyList<string>> FindByNameAsync(
        string fileName,
        SearchOptions options,
        IProgress<SearchProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One progress tick from a search operation.
/// </summary>
public sealed record SearchProgress(
    SearchTier Tier,
    int DirectoriesScanned,
    int MatchesFound);

/// <summary>
/// The tiers a search implementation may walk through, cheapest first.
/// </summary>
public enum SearchTier
{
    Indexed = 0,
    MetadataScan = 1,
    RecursiveWalk = 2,
}
