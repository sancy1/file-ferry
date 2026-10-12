// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Dispatch/DispatchingFileResolver.cs
// PURPOSE:  The composition seam between local and cloud resolution.
//           Implements IFileResolver by routing each request to the
//           local resolver or the cloud resolver, based on
//           PathScheme.IsCloudPath. Merges the two result sets and
//           reports dispatcher-level progress.
// RELATION: Implements IFileResolver. Registered as the primary
//           IFileResolver in C.3.4. Takes ILocalFileResolver and
//           ICloudFileResolver.
// ============================================================================

using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Services;

namespace FileHarvester.Platform.Cloud.Dispatch;

/// <summary>
/// Routes FileRequests to the local or cloud resolver by scheme.
/// </summary>
/// <remarks>
/// <para>
/// Routing is by <see cref="PathScheme.IsCloudPath"/>. The predicate
/// returns true only for s3, gs, gdrive, azure, and dropbox.
/// file:// is recognized but not cloud; it routes local.
/// </para>
/// <para>
/// Execution is sequential: the local branch runs to completion
/// before the cloud branch starts.
/// </para>
/// <para>
/// Results are merged by original request Index. Order is
/// preserved. Duplicates within a request (as the local resolver
/// produces for ambiguous bare filenames) are preserved.
/// </para>
/// <para>
/// Progress is reported by the dispatcher only, once after each
/// completed branch. The underlying resolvers receive
/// <c>progress: null</c>. This makes the caller's progress totals
/// reflect the full batch, not a sub-batch.
/// </para>
/// <para>
/// Exceptions from either branch propagate. A cancelled token throws
/// <see cref="OperationCanceledException"/> at the next phase
/// boundary.
/// </para>
/// </remarks>
public sealed class DispatchingFileResolver : IFileResolver
{
    private readonly ILocalFileResolver _local;
    private readonly ICloudFileResolver _cloud;

    /// <summary>
    /// Creates the dispatcher.
    /// </summary>
    /// <param name="local">The local resolver. Never null.</param>
    /// <param name="cloud">The cloud resolver. Never null.</param>
    public DispatchingFileResolver(
        ILocalFileResolver local,
        ICloudFileResolver cloud)
    {
        _local = local ?? throw new ArgumentNullException(nameof(local));
        _cloud = cloud ?? throw new ArgumentNullException(nameof(cloud));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ResolvedFile>> ResolveAsync(
        IReadOnlyList<FileRequest> requests,
        IProgress<ResolveProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        ResolveAsync(requests, SearchOptions.Default, progress, cancellationToken);

    /// <inheritdoc/>
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

        var localRequests = new List<FileRequest>();
        var cloudRequests = new List<FileRequest>();

        foreach (var r in requests)
        {
            if (PathScheme.IsCloudPath(r.RawInput))
                cloudRequests.Add(r);
            else
                localRequests.Add(r);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var localCollector = new NoteCollector();

        var localResults = localRequests.Count > 0
            ? await _local.ResolveAsync(localRequests, searchOptions, localCollector, cancellationToken)
                .ConfigureAwait(false)
            : (IReadOnlyList<ResolvedFile>)Array.Empty<ResolvedFile>();

        if (localRequests.Count > 0)
        {
            var localResolvedIndices = new HashSet<int>();
            foreach (var r in localResults) localResolvedIndices.Add(r.Request.Index);

            var localResolvedAll = true;
            foreach (var r in localRequests)
            {
                if (!localResolvedIndices.Contains(r.Index))
                {
                    localResolvedAll = false;
                    break;
                }
            }

            progress?.Report(new ResolveProgress(
                Index: localRequests[localRequests.Count - 1].Index,
                Total: requests.Count,
                Processed: localRequests.Count,
                Status: localResolvedAll ? FileStatus.Found : FileStatus.NotFound,
                Note: localCollector.LastNote));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var cloudCollector = new NoteCollector();

        var cloudResults = cloudRequests.Count > 0
            ? await _cloud.ResolveAsync(cloudRequests, cloudCollector, cancellationToken)
                .ConfigureAwait(false)
            : (IReadOnlyList<ResolvedFile>)Array.Empty<ResolvedFile>();

        if (cloudRequests.Count > 0)
        {
            var cloudResolvedIndices = new HashSet<int>();
            foreach (var r in cloudResults) cloudResolvedIndices.Add(r.Request.Index);

            var cloudResolvedAll = true;
            foreach (var r in cloudRequests)
            {
                if (!cloudResolvedIndices.Contains(r.Index))
                {
                    cloudResolvedAll = false;
                    break;
                }
            }

            progress?.Report(new ResolveProgress(
                Index: cloudRequests[cloudRequests.Count - 1].Index,
                Total: requests.Count,
                Processed: localRequests.Count + cloudRequests.Count,
                Status: cloudResolvedAll ? FileStatus.Found : FileStatus.NotFound,
                Note: cloudCollector.LastNote));
        }

        return Merge(localResults, cloudResults);
    }

    private static IReadOnlyList<ResolvedFile> Merge(
        IReadOnlyList<ResolvedFile> local,
        IReadOnlyList<ResolvedFile> cloud)
    {
        if (local.Count == 0) return cloud;
        if (cloud.Count == 0) return local;

        var byIndex = new SortedDictionary<int, List<ResolvedFile>>();
        foreach (var r in local)
        {
            if (!byIndex.TryGetValue(r.Request.Index, out var list))
            {
                list = new List<ResolvedFile>();
                byIndex[r.Request.Index] = list;
            }
            list.Add(r);
        }
        foreach (var r in cloud)
        {
            if (!byIndex.TryGetValue(r.Request.Index, out var list))
            {
                list = new List<ResolvedFile>();
                byIndex[r.Request.Index] = list;
            }
            list.Add(r);
        }

        var merged = new List<ResolvedFile>(local.Count + cloud.Count);
        foreach (var kvp in byIndex)
        {
            merged.AddRange(kvp.Value);
        }
        return merged;
    }

    // ------------------------------------------------------------------
    // NoteCollector
    // ------------------------------------------------------------------

    /// <summary>
    /// Collects the last non-null Note from a progress stream, ignoring
    /// all other fields. Used to carry the underlying resolver's notes
    /// into the dispatcher's per-phase summary tick without forwarding
    /// the underlying ticks to the caller.
    /// </summary>
    private sealed class NoteCollector : IProgress<ResolveProgress>
    {
        /// <summary>The last non-null Note reported, or null.</summary>
        public string? LastNote { get; private set; }

        /// <inheritdoc/>
        public void Report(ResolveProgress value)
        {
            if (!string.IsNullOrWhiteSpace(value.Note))
            {
                LastNote = value.Note;
            }
        }
    }

}
