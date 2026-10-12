// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Dispatch/DispatchingContentReader.cs
// PURPOSE:  The composition seam between local and cloud reads.
//           Implements IContentReader by routing each ResolvedFile to
//           the local reader or the cloud reader, based on
//           PathScheme.IsCloudPath. Merges results in input order.
// RELATION: Implements IContentReader. Registered as the primary
//           IContentReader in C.3.4. Takes ILocalContentReader and
//           ICloudContentReader.
// ============================================================================

using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Services;

namespace FileHarvester.Platform.Cloud.Dispatch;

/// <summary>
/// Routes ResolvedFiles to the local or cloud reader by scheme.
/// </summary>
/// <remarks>
/// <para>
/// Routing is by <see cref="PathScheme.IsCloudPath"/> on
/// <c>file.AbsolutePath</c>.
/// </para>
/// <para>
/// Execution is sequential: local branch, then cloud branch.
/// </para>
/// <para>
/// The merged result contains exactly one FileContent per input
/// file, in input order, matching the IContentReader contract. If an
/// underlying reader returns a list that does not cover every input
/// file, the dispatcher inserts a
/// <see cref="FileContent.Unreadable"/> placeholder for the missing
/// entry. A reader that throws is not covered by this placeholder:
/// the exception propagates.
/// </para>
/// </remarks>
public sealed class DispatchingContentReader : IContentReader
{
    /// <summary>
    /// The placeholder reason inserted for a missing read result.
    /// </summary>
    public const string MissingResultReason =
        "Dispatcher: no result from reader for this file.";

    private readonly ILocalContentReader _local;
    private readonly ICloudContentReader _cloud;

    /// <summary>
    /// Creates the dispatcher.
    /// </summary>
    public DispatchingContentReader(
        ILocalContentReader local,
        ICloudContentReader cloud)
    {
        _local = local ?? throw new ArgumentNullException(nameof(local));
        _cloud = cloud ?? throw new ArgumentNullException(nameof(cloud));
    }

    /// <inheritdoc/>
    public async Task<FileContent> ReadAsync(
        ResolvedFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        return PathScheme.IsCloudPath(file.AbsolutePath)
            ? await _cloud.ReadAsync(file, cancellationToken).ConfigureAwait(false)
            : await _local.ReadAsync(file, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FileContent>> ReadManyAsync(
        IReadOnlyList<ResolvedFile> files,
        IProgress<ReadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (files.Count == 0)
        {
            return Array.Empty<FileContent>();
        }

        var localFiles = new List<ResolvedFile>();
        var cloudFiles = new List<ResolvedFile>();

        foreach (var f in files)
        {
            if (PathScheme.IsCloudPath(f.AbsolutePath))
                cloudFiles.Add(f);
            else
                localFiles.Add(f);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var localResults = localFiles.Count > 0
            ? await _local.ReadManyAsync(localFiles, null, cancellationToken)
                .ConfigureAwait(false)
            : (IReadOnlyList<FileContent>)Array.Empty<FileContent>();

        cancellationToken.ThrowIfCancellationRequested();

        var cloudResults = cloudFiles.Count > 0
            ? await _cloud.ReadManyAsync(cloudFiles, null, cancellationToken)
                .ConfigureAwait(false)
            : (IReadOnlyList<FileContent>)Array.Empty<FileContent>();

        // Build a lookup by the input file's request Index.
        var byIndex = new Dictionary<int, FileContent>();
        foreach (var c in localResults) byIndex[c.File.Request.Index] = c;
        foreach (var c in cloudResults) byIndex[c.File.Request.Index] = c;

        // Merge in input order, one per input.
        var merged = new List<FileContent>(files.Count);
        var processed = 0;
        var totalBytes = 0L;

        for (var i = 0; i < files.Count; i++)
        {
            var f = files[i];

            FileContent content;
            if (!byIndex.TryGetValue(f.Request.Index, out content!))
            {
                content = FileContent.Unreadable(f, MissingResultReason);
            }

            merged.Add(content);
            processed++;
            totalBytes += content.RawBytes.Length;

            progress?.Report(new ReadProgress(
                Index: i,
                Total: files.Count,
                Processed: processed,
                BytesRead: totalBytes,
                Status: content.IsUnreadable
                    ? FileStatus.ReadError
                    : FileStatus.Found));
        }

        return merged;
    }
}
