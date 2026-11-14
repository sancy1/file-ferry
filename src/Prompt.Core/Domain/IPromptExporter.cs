// ============================================================================
// FILE:     src/Prompt.Core/Domain/IPromptExporter.cs
// PURPOSE:  Contract for exporting one prompt to a file in the requested
//           format. The write is wrapped in the prompt-export resilience
//           policy; an existing file prompts the user before being replaced.
// RELATION: Implemented by PromptExporter (P3.2, Prompt.Platform.Export).
//           Consumed by PromptExportDialogViewModel (P3.4). Uses ExportResult
//           for both success and failure - failures are values, not exceptions.
// NOTES:    - ExportSingleAsync returns ExportResult.Ok on success,
//             ExportResult.Fail on any error (unknown extension, folder
//             creation failure, user cancel, write failure, circuit open).
//           - The exporter opens the FileStream itself; the IFormatWriter
//             never touches disk.
//           - The prompt-export policy (PromptPolicyNames.PromptExport) wraps
//             the whole export operation, including the existence check and
//             the user prompt.
// ============================================================================

namespace Prompt.Core.Domain;

/// <summary>
/// Exports one prompt to a file in the requested format.
/// </summary>
public interface IPromptExporter
{
    /// <summary>
    /// Exports a single prompt to the destination folder in the requested
    /// extension's format. Prompts before overwriting an existing file.
    /// Returns <see cref="ExportResult.Ok"/> on success,
    /// <see cref="ExportResult.Fail"/> on any error.
    /// </summary>
    /// <param name="prompt">The prompt to export. Never null.</param>
    /// <param name="extension">
    /// The output extension including the leading dot (e.g. ".txt"). The
    /// IFormatWriterRegistry resolves the writer; an unregistered extension
    /// produces an ExportResult.Fail.
    /// </param>
    /// <param name="destinationFolder">
    /// The folder to write the file into. Created if it does not exist.
    /// </param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>The export result.</returns>
    Task<ExportResult> ExportSingleAsync(
        Prompt prompt,
        string extension,
        string destinationFolder,
        string? outputBaseName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports many prompts into a single zip archive. Each prompt is
    /// written through its IFormatWriter into a per-entry memory stream;
    /// the entries are organized by Category/SubCategory/Title.ext (empty
    /// category -> Uncategorized/). A manifest.json describes the export.
    /// Returns <see cref="ExportResult.Ok"/> on success, with OutputPath
    /// naming the produced zip.
    /// </summary>
    /// <param name="prompts">The prompts to export. Never null; may be empty.</param>
    /// <param name="extension">
    /// The per-prompt output extension including the leading dot (e.g. ".txt").
    /// The IFormatWriterRegistry resolves the writer once for all prompts.
    /// </param>
    /// <param name="destinationFolder">
    /// The folder to write the zip into. Created if it does not exist.
    /// </param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>The export result; the OutputPath is the zip's path.</returns>
    Task<ExportResult> ExportManyAsync(
        IReadOnlyList<Prompt> prompts,
        string extension,
        string destinationFolder,
        string? zipBaseName = null,
        CancellationToken cancellationToken = default);
}