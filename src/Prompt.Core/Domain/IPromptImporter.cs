// ============================================================================
// FILE:     src/Prompt.Core/Domain/IPromptImporter.cs
// PURPOSE:  Contract for importing a source file (.txt, .md, .docx, .pdf)
//           as a new prompt. The extraction dispatches by file extension; the
//           created Prompt is populated and returned on success. Failures
//           are values (ImportResult.Fail), not exceptions.
// RELATION: Implemented by PromptImporter (P3.5, Prompt.Platform.Import).
//           Consumed by PromptImportDialogViewModel (P3.6). Uses ImportResult
//           for both success and failure.
// NOTES:    - Supported extensions: .txt, .md (direct read), .docx (via
//             DocumentFormat.OpenXml), .pdf (via IPdfTextExtractor).
//           - The title is the source file's name without extension.
//           - The body's BodyFormat is PlainText; the file's original format
//             is preserved only as the body text.
//           - The created prompt is stored via IPromptStore.CreateAsync.
// ============================================================================

namespace Prompt.Core.Domain;

/// <summary>
/// Imports a source file as a new prompt.
/// </summary>
public interface IPromptImporter
{
    /// <summary>
    /// Imports the file at <paramref name="sourceFilePath"/> as a new prompt
    /// in the given category / sub-category. Returns
    /// <see cref="ImportResult.Ok"/> on success, <see cref="ImportResult.Fail"/>
    /// on any error (missing file, unsupported extension, extraction failure,
    /// store failure).
    /// </summary>
    /// <param name="sourceFilePath">The source file's absolute path.</param>
    /// <param name="categoryId">The category to place the prompt in, or null.</param>
    /// <param name="subCategoryId">The sub-category, or null.</param>
    /// <param name="cancellationToken">Cancels the import.</param>
    /// <returns>The import result.</returns>
    Task<ImportResult> ImportFileAsync(
        string sourceFilePath,
        Guid? categoryId,
        Guid? subCategoryId,
        CancellationToken cancellationToken = default);
}