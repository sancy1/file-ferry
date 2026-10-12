// ============================================================================
// FILE:     src\FileHarvester.Core/Implementation/FileResolver.cs
// PURPOSE:  Implementation of IFileResolver. Turns each FileRequest into
//           zero or more ResolvedFile records by trying, in order:
//             1. The input as an absolute path.
//             2. The input as a relative path against a hinted directory.
//             3. The input as a relative path against common OS folders.
//             4. Whole-PC search via IPathSearchService (bare filenames).
//           Every filesystem call runs through the "file-read" policy;
//           every search runs through "path-search". Search options control
//           whether matching is exact name or base name only.
// RELATION: Implements IFileResolver (Phase 1.0). Registered in
//           AddFileHarvester. Depends on IResilienceExecutor,
//           IPathSearchService, IDefaultPathsProvider.
// ============================================================================

using FileHarvester.Core.Composition;
using FileHarvester.Core.Domain;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.Core.Implementation;

public sealed class FileResolver : IFileResolver, ILocalFileResolver
{
    private readonly IResilienceExecutor _resilience;
    private readonly IPathSearchService _pathSearch;
    private readonly IDefaultPathsProvider _paths;

    public FileResolver(
        IResilienceExecutor resilience,
        IPathSearchService pathSearch,
        IDefaultPathsProvider paths)
    {
        _resilience = resilience ?? throw new ArgumentNullException(nameof(resilience));
        _pathSearch = pathSearch ?? throw new ArgumentNullException(nameof(pathSearch));
        _paths      = paths      ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ResolvedFile>> ResolveAsync(
        IReadOnlyList<FileRequest> requests,
        IProgress<ResolveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Default: ExactName. The UI can pass a richer SearchOptions when
        // the user has selected BaseName mode in the Input panel.
        return await ResolveAsync(
            requests,
            SearchOptions.Default,
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves with explicit search options. BaseName mode expands a
    /// single "note" into matches for note.txt, note.pdf, note.png, etc.
    /// </summary>
    public async Task<IReadOnlyList<ResolvedFile>> ResolveAsync(
        IReadOnlyList<FileRequest> requests,
        SearchOptions searchOptions,
        IProgress<ResolveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(searchOptions);

        if (requests.Count == 0)
        {
            return Array.Empty<ResolvedFile>();
        }

        var resolved = new List<ResolvedFile>(requests.Count);
        var processed = 0;

        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await ResolveOneAsync(request, searchOptions, cancellationToken)
                .ConfigureAwait(false);

            resolved.AddRange(result.Files);
            processed++;

            progress?.Report(new ResolveProgress(
                Index:        request.Index,
                Total:        requests.Count,
                Processed:    processed,
                Status:       result.Status,
                ResolvedPath: result.Files.Count > 0 ? result.Files[0].AbsolutePath : null));
        }

        return resolved;
    }

    // -----------------------------------------------------------------
    // Per-request resolution.
    // -----------------------------------------------------------------

    private sealed record OneResult(
        IReadOnlyList<ResolvedFile> Files,
        FileStatus Status);

    private async Task<OneResult> ResolveOneAsync(
        FileRequest request,
        SearchOptions searchOptions,
        CancellationToken cancellationToken)
    {
        var raw = request.RawInput;

        // ---- 1. Absolute path --------------------------------------
        if (Path.IsPathRooted(raw))
        {
            var file = await TryProbeAsync(
                request,
                raw,
                ResolutionConfidence.Exact,
                cancellationToken).ConfigureAwait(false);

            if (file is not null)
            {
                return new OneResult(new[] { file }, FileStatus.Found);
            }

            return new OneResult(Array.Empty<ResolvedFile>(), FileStatus.NotFound);
        }

        // ---- 2. Relative path against hinted directory -------------
        if (!string.IsNullOrWhiteSpace(request.HintedDirectory))
        {
            var candidate = SafeCombine(request.HintedDirectory!, raw);
            if (candidate is not null)
            {
                var file = await TryProbeAsync(
                    request,
                    candidate,
                    ResolutionConfidence.Relative,
                    cancellationToken).ConfigureAwait(false);

                if (file is not null)
                {
                    return new OneResult(new[] { file }, FileStatus.Found);
                }
            }
        }

        // ---- 3. Relative path against common roots -----------------
        foreach (var root in _paths.CommonSearchRoots)
        {
            var candidate = SafeCombine(root, raw);
            if (candidate is null) continue;

            var file = await TryProbeAsync(
                request,
                candidate,
                ResolutionConfidence.Relative,
                cancellationToken).ConfigureAwait(false);

            if (file is not null)
            {
                return new OneResult(new[] { file }, FileStatus.Found);
            }
        }

        // ---- 4. Whole-PC search ------------------------------------
        var fileName = Path.GetFileName(raw);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return new OneResult(Array.Empty<ResolvedFile>(), FileStatus.NotFound);
        }

        var matches = await SearchAsync(fileName, searchOptions, cancellationToken)
            .ConfigureAwait(false);

        if (matches.Count == 0)
        {
            return new OneResult(Array.Empty<ResolvedFile>(), FileStatus.NotFound);
        }

        var confidence = matches.Count == 1
            ? ResolutionConfidence.Searched
            : ResolutionConfidence.Ambiguous;

        var resolvedMatches = new List<ResolvedFile>(matches.Count);
        foreach (var match in matches)
        {
            var file = await TryProbeAsync(
                request,
                match,
                confidence,
                cancellationToken).ConfigureAwait(false);

            if (file is not null)
            {
                resolvedMatches.Add(file);
            }
        }

        if (resolvedMatches.Count == 0)
        {
            return new OneResult(Array.Empty<ResolvedFile>(), FileStatus.NotFound);
        }

        var status = resolvedMatches.Count == 1
            ? FileStatus.Found
            : FileStatus.Ambiguous;

        return new OneResult(resolvedMatches, status);
    }

    // -----------------------------------------------------------------
    // Filesystem calls — every one wrapped in "file-read" policy.
    // -----------------------------------------------------------------

    private async Task<ResolvedFile?> TryProbeAsync(
        FileRequest request,
        string path,
        ResolutionConfidence confidence,
        CancellationToken cancellationToken)
    {
        var info = await _resilience.ExecuteAsync(
            policyName: PolicyNames.FileRead,
            operation: _ =>
            {
                var fi = new FileInfo(path);
                return Task.FromResult(fi.Exists ? fi : null);
            },
            fallback: _ => Task.FromResult<FileInfo?>(null),
            ct: cancellationToken).ConfigureAwait(false);

        if (info is null)
        {
            return null;
        }

        return new ResolvedFile(
            Request:         request,
            AbsolutePath:    info.FullName,
            SizeBytes:       info.Length,
            LastModifiedUtc: info.LastWriteTimeUtc,
            Confidence:      confidence);
    }

    private async Task<IReadOnlyList<string>> SearchAsync(
        string fileName,
        SearchOptions options,
        CancellationToken cancellationToken)
    {
        return await _resilience.ExecuteAsync(
            policyName: PolicyNames.PathSearch,
            operation: token => _pathSearch.FindByNameAsync(
                fileName,
                options,
                progress: null,
                cancellationToken: token),
            fallback: _ => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()),
            ct: cancellationToken).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    // Helpers.
    // -----------------------------------------------------------------

    private static string? SafeCombine(string directory, string relative)
    {
        try
        {
            return Path.GetFullPath(Path.Combine(directory, relative));
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (PathTooLongException)
        {
            return null;
        }
    }
}
