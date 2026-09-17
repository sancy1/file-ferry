// ============================================================================
// FILE:     src\FileHarvester.Core/Domain/SaveResult.cs
// PURPOSE:  Result of a save operation. Carries the destination, counts,
//           bytes written, and any error. Used by the harvest job runner
//           to report success or failure to the UI.
// RELATION: Returned by IOutputSink.SaveAsync. Consumed by HarvestJobRunner
//           and by the HarvestResultDialog.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// The outcome of writing a bundle to disk.
/// </summary>
/// <param name="Success">True when the write completed without error.</param>
/// <param name="DestinationFolder">The folder the output was written to.</param>
/// <param name="OutputPath">
/// The absolute path of the primary output file (single-merged) or the
/// destination folder (separate files). Null on failure.
/// </param>
/// <param name="FileCount">Number of source files included in the output.</param>
/// <param name="BytesWritten">Total bytes written to disk.</param>
/// <param name="ErrorMessage">Populated when <see cref="Success"/> is false.</param>
public sealed record SaveResult(
    bool Success,
    string DestinationFolder,
    string? OutputPath,
    int FileCount,
    long BytesWritten,
    string? ErrorMessage = null)
{
    /// <summary>Builds a successful result.</summary>
    public static SaveResult Ok(
        string destinationFolder,
        string outputPath,
        int fileCount,
        long bytesWritten) =>
        new(true, destinationFolder, outputPath, fileCount, bytesWritten);

    /// <summary>Builds a failed result.</summary>
    public static SaveResult Fail(
        string destinationFolder,
        string reason,
        int fileCount = 0,
        long bytesWritten = 0) =>
        new(false, destinationFolder, null, fileCount, bytesWritten, reason);
}
