// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Dispatch/DispatchingOutputSink.cs
// PURPOSE:  The composition seam between local and cloud writes.
//           Implements IOutputSink by routing to the local sink or the
//           cloud sink, based on PathScheme.IsCloudPath on
//           options.DestinationFolder.
// RELATION: Implements IOutputSink. Registered as the primary
//           IOutputSink in C.3.4. Takes ILocalOutputSink and
//           ICloudOutputSink.
// ============================================================================

using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Services;

namespace FileHarvester.Platform.Cloud.Dispatch;

/// <summary>
/// Routes save requests to the local or cloud sink by destination.
/// </summary>
/// <remarks>
/// <para>
/// Routing is by <see cref="PathScheme.IsCloudPath"/> on
/// <c>options.DestinationFolder</c>. A null, empty, or whitespace
/// destination routes local. A file:// URI routes local (it is
/// recognized but not cloud). A recognized cloud scheme (s3, gs,
/// gdrive, azure, dropbox) routes cloud. An unrecognized scheme
/// routes local.
/// </para>
/// </remarks>
public sealed class DispatchingOutputSink : IOutputSink
{
    private readonly ILocalOutputSink _local;
    private readonly ICloudOutputSink _cloud;

    /// <summary>
    /// Creates the dispatcher.
    /// </summary>
    public DispatchingOutputSink(
        ILocalOutputSink local,
        ICloudOutputSink cloud)
    {
        _local = local ?? throw new ArgumentNullException(nameof(local));
        _cloud = cloud ?? throw new ArgumentNullException(nameof(cloud));
    }

    /// <inheritdoc/>
    public Task<SaveResult> SaveAsync(
        ComposedDocument document,
        OutputSinkOptions options,
        IProgress<SaveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        return PathScheme.IsCloudPath(options.DestinationFolder)
            ? _cloud.SaveAsync(document, options, progress, cancellationToken)
            : _local.SaveAsync(document, options, progress, cancellationToken);
    }
}
