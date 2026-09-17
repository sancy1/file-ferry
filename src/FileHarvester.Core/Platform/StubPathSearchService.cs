// ============================================================================
// FILE:     src\FileHarvester.Core/Platform/StubPathSearchService.cs
// PURPOSE:  No-op IPathSearchService used on non-Windows platforms until
//           Phase 5.1 delivers MacPathSearch and LinuxPathSearch.
// RELATION: Registered by AddFileHarvester when the OS is not Windows.
//           Returns empty for every search.
// ============================================================================

using FileHarvester.Core.Domain;

namespace FileHarvester.Core.Platform;

/// <summary>
/// No-op IPathSearchService. Always returns an empty list.
/// </summary>
public sealed class StubPathSearchService : IPathSearchService
{
    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> FindByNameAsync(
        string fileName,
        SearchOptions options,
        IProgress<SearchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new SearchProgress(
            Tier: SearchTier.Indexed,
            DirectoriesScanned: 0,
            MatchesFound: 0));

        return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}
