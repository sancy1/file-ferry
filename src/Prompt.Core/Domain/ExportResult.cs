// ============================================================================
// FILE:     src/Prompt.Core/Domain/ExportResult.cs
// PURPOSE:  Outcome of exporting one or more prompts. Carries success, the
//           primary output path, the number of prompts exported, and any
//           error message.
// RELATION: Returned by IPromptExporter.ExportSingleAsync / ExportManyAsync
//           (P3.2 / P3.3). Consumed by PromptExportDialogViewModel (P3.4).
// NOTES:    Failures are values, not exceptions. On success OutputPath names
//           the produced file (single) or the produced zip (multi). The
//           per-file count is provided even on success so the UI can report
//           "exported N prompts." Follows the Ok/Fail factory pattern
//           established by SaveResult in FileHarvester.Core.
// ============================================================================

namespace Prompt.Core.Domain;

/// <summary>
/// Outcome of exporting one or more prompts.
/// </summary>
/// <param name="Success">True when the write completed.</param>
/// <param name="OutputPath">The produced file path, or null on failure.</param>
/// <param name="FileCount">Number of prompts exported.</param>
/// <param name="ErrorMessage">Populated when <see cref="Success"/> is false.</param>
public sealed record ExportResult(
    bool Success,
    string? OutputPath,
    int FileCount,
    string? ErrorMessage = null)
{
    /// <summary>Builds a successful export result.</summary>
    public static ExportResult Ok(string outputPath, int fileCount) =>
        new(true, outputPath, fileCount);

    /// <summary>Builds a failed export result.</summary>
    public static ExportResult Fail(string reason) => new(false, null, 0, reason);
}