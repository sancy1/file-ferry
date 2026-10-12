// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/IGDriveExportClient.cs
// PURPOSE:  The Drive-specific export abstraction. A Google-native document
//           (Doc, Sheet, Slide, Drawing) has no downloadable binary content
//           and must be exported to a concrete MIME type. This interface is
//           the seam the caller uses after catching
//           GoogleNativeFileRequiresExportException. It is separate from
//           ICloudProvider so that the generic contract does not gain a
//           provider-specific export method.
// RELATION: Implemented by GDriveClientAdapter (which also implements
//           IGDriveClient). Consumed by the caller that decides the export
//           format (C.8 UI). Internal for consistency with the wrapper family.
// ============================================================================

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// Exports a Google-native file to a concrete MIME type.
/// </summary>
/// <remarks>
/// <para>
/// The caller supplies the export MIME type explicitly. There is no
/// default: the provider never silently substitutes one, and the
/// exception carries only the file id, not a format list. A separate
/// capability query for the supported formats is out of scope for C.6.5
/// and is recorded as a possible future addition.
/// </para>
/// <para>
/// <b>Cancellation contract.</b> Caller cancellation throws
/// <see cref="OperationCanceledException"/> unchanged.
/// </para>
/// <para>
/// <b>Stream ownership.</b> The returned stream is owned by the caller.
/// </para>
/// </remarks>
internal interface IGDriveExportClient
{
    /// <summary>
    /// Exports a Google-native file to the requested MIME type and returns
    /// the exported bytes as a stream.
    /// </summary>
    /// <param name="fileId">The Drive file id. Non-null, non-empty.</param>
    /// <param name="exportMimeType">
    /// The MIME type to export to (for example <c>application/pdf</c>).
    /// Non-null, non-empty. The caller chooses it; there is no default.
    /// </param>
    /// <param name="ct">Cancels the export.</param>
    /// <returns>A readable stream owned by the caller, positioned at 0.</returns>
    Task<Stream> ExportAsync(
        string fileId,
        string exportMimeType,
        CancellationToken ct);
}
