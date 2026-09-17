// ============================================================================
// FILE:     src\FileHarvester.Core/Platform/WindowsPathSearch.cs
// PURPOSE:  Windows implementation of IPathSearchService. Enumerates every
//           fixed drive and delegates to RecursiveWalkSearch.
// RELATION: Registered in AddFileHarvester on Windows. Called by FileResolver.
// ============================================================================

using FileHarvester.Core.Composition;
using FileHarvester.Core.Domain;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.Core.Platform;

public sealed class WindowsPathSearch : IPathSearchService
{
    private readonly IResilienceExecutor _resilience;

    public WindowsPathSearch(IResilienceExecutor resilience)
    {
        _resilience = resilience ?? throw new ArgumentNullException(nameof(resilience));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> FindByNameAsync(
        string fileName,
        SearchOptions options,
        IProgress<SearchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(options);

        var roots = EnumerateFixedDrives();

        progress?.Report(new SearchProgress(
            Tier: SearchTier.RecursiveWalk,
            DirectoriesScanned: 0,
            MatchesFound: 0));

        var allMatches = new List<string>();

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var matches = await _resilience.ExecuteAsync(
                    policyName: PolicyNames.PathSearch,
                    operation: async token => (IReadOnlyList<string>)await RecursiveWalkSearch.WalkAsync(
                        fileName,
                        new[] { root },
                        options,
                        progress,
                        token).ConfigureAwait(false),
                    fallback: _ => Task.FromResult<IReadOnlyList<string>>(
                        Array.Empty<string>()),
                    ct: cancellationToken).ConfigureAwait(false);

                allMatches.AddRange(matches);

                if (allMatches.Count >= options.MaxResults)
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return allMatches;
    }

    private static List<string> EnumerateFixedDrives()
    {
        var drives = new List<string>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                    {
                        drives.Add(drive.RootDirectory.FullName);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }

        return drives;
    }
}
