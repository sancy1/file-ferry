// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Services/ICloudOutputSink.cs
// PURPOSE:  Contract for writing a composed document to a cloud
//           destination. Mirrors IOutputSink. The dispatcher routes
//           cloud destinations here; local destinations go to
//           OutputSink.
// RELATION: Implemented by CloudOutputSink. Consumed by
//           DispatchingOutputSink (C.3.3).
// ============================================================================

using FileHarvester.Core.Domain;

namespace FileHarvester.Platform.Cloud.Services;

/// <summary>
/// Writes a composed document to a cloud destination.
/// </summary>
/// <remarks>
/// <para>
/// The contract mirrors <see cref="IOutputSink"/>. The destination
/// in <see cref="OutputSinkOptions.DestinationFolder"/> is a
/// URI-shaped cloud path.
/// </para>
/// </remarks>
public interface ICloudOutputSink
{
    /// <summary>
    /// Writes the document to the cloud destination.
    /// </summary>
    /// <param name="document">The composed document.</param>
    /// <param name="options">
    /// The output options. DestinationFolder must be a cloud URI.
    /// </param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// A <see cref="SaveResult"/>. The DestinationFolder and OutputPath
    /// fields carry the URI-shaped destination.
    /// </returns>
    Task<SaveResult> SaveAsync(
        ComposedDocument document,
        OutputSinkOptions options,
        IProgress<SaveProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
