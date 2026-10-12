// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Discovery/LocalCatalogResolver.cs
// PURPOSE:  The local layer of name resolution. Queries the in-memory
//           CloudNameCatalog for alias matches. Offline, deterministic, no
//           I/O, no policy. This is the default resolver.
// RELATION: Implements ICloudNameResolver. Consumes CloudNameCatalog.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.Discovery;

/// <summary>
/// Resolves a name against the bundled catalog.
/// </summary>
public sealed class LocalCatalogResolver : ICloudNameResolver
{
    private readonly CloudNameCatalog _catalog;

    /// <summary>
    /// Creates the resolver over a catalog.
    /// </summary>
    /// <param name="catalog">The catalog. Not null.</param>
    public LocalCatalogResolver(CloudNameCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CloudNameCandidate>> ResolveAsync(
        string userInput,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            return Task.FromResult<IReadOnlyList<CloudNameCandidate>>(
                Array.Empty<CloudNameCandidate>());
        }

        cancellationToken.ThrowIfCancellationRequested();

        var needle = userInput.Trim();
        var results = new List<CloudNameCandidate>();

        foreach (var entry in _catalog.Entries)
        {
            var (matched, confidence) = MatchEntry(needle, entry);
            if (!matched)
            {
                continue;
            }

            results.Add(new CloudNameCandidate(
                CanonicalName: entry.CanonicalName,
                ProviderId: entry.ProviderId,
                Aliases: entry.Aliases,
                AuthKind: entry.AuthKind,
                OfficialUrl: entry.OfficialUrl,
                Source: CloudNameSource.Local,
                Confidence: confidence));
        }

        return Task.FromResult<IReadOnlyList<CloudNameCandidate>>(results);
    }

    /// <summary>
    /// Matches the trimmed input against one entry and its aliases. Returns
    /// Exact when the input equals an alias, Strong when one contains the
    /// other. Case-insensitive ordinal.
    /// </summary>
    private static (bool Matched, CloudNameConfidence Confidence) MatchEntry(
        string needle,
        CloudNameCatalog.CatalogEntry entry)
    {
        if (string.Equals(needle, entry.CanonicalName, StringComparison.OrdinalIgnoreCase))
        {
            return (true, CloudNameConfidence.Exact);
        }

        var bestConfidence = (CloudNameConfidence?)null;

        foreach (var alias in entry.Aliases)
        {
            if (string.Equals(needle, alias, StringComparison.OrdinalIgnoreCase))
            {
                return (true, CloudNameConfidence.Exact);
            }

            if (alias.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || needle.Contains(alias, StringComparison.OrdinalIgnoreCase))
            {
                if (bestConfidence is null || bestConfidence == CloudNameConfidence.Weak)
                {
                    bestConfidence = CloudNameConfidence.Strong;
                }
            }
        }

        return bestConfidence is null
            ? (false, CloudNameConfidence.Weak)
            : (true, bestConfidence.Value);
    }
}
