// ============================================================================
// FILE:     src/Prompt.Platform/Import/PromptImporter.cs
// PURPOSE:  Imports a source file (.txt, .md, .docx, .pdf) as a new prompt.
//           The extraction dispatches by file extension; the .txt/.md are
//           direct reads, the .docx uses DocumentFormat.OpenXml, the .pdf
//           uses IPdfTextExtractor. The created prompt is stored via
//           IPromptStore.CreateAsync.
// RELATION: Implements IPromptImporter (Prompt.Core.Domain). Consumes
//           IPromptStore, IPdfTextExtractor, and DocumentFormat.OpenXml.
//           Consumed by PromptImportDialogViewModel (P3.6).
// NOTES:    - The title is the source file's name without extension.
//           - The body's BodyFormat is PlainText.
//           - Failures are ImportResult.Fail values, not exceptions; the
//             importer catches the extraction and store exceptions.
//           - The .docx extraction joins the paragraphs with newlines (the
//             body.InnerText does not preserve the paragraph breaks).
// ============================================================================

using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Prompt.Core.Domain;
using Prompt.Platform.Observability;
using PromptRecord = Prompt.Core.Domain.Prompt;

namespace Prompt.Platform.Import;

/// <summary>
/// Imports a source file as a new prompt.
/// </summary>
public sealed class PromptImporter : IPromptImporter
{
    private readonly IPromptStore _store;
    private readonly IPdfTextExtractor _pdfExtractor;
    private readonly IPromptEventEmitter _events;

    /// <summary>Creates the importer. Both collaborators are required.</summary>
    /// <param name="events">
    /// The operation-event emitter. When null, <see cref="NullPromptEventEmitter.Instance"/>
    /// is used so the importer can emit unconditionally without null checks.
    /// </param>
    public PromptImporter(IPromptStore store, IPdfTextExtractor pdfExtractor, IPromptEventEmitter? events = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _pdfExtractor = pdfExtractor ?? throw new ArgumentNullException(nameof(pdfExtractor));
        _events = events ?? NullPromptEventEmitter.Instance;
    }

    /// <inheritdoc/>
    public async Task<ImportResult> ImportFileAsync(
        string sourceFilePath,
        Guid? categoryId,
        Guid? subCategoryId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        if (!File.Exists(sourceFilePath))
        {
            return ImportResult.Fail("File not found: " + sourceFilePath);
        }

        var extension = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        var title = Path.GetFileNameWithoutExtension(sourceFilePath);

        string body;
        try
        {
            body = extension switch
            {
                ".txt" or ".md" => await File.ReadAllTextAsync(sourceFilePath, cancellationToken).ConfigureAwait(false),
                ".docx" => ExtractDocxText(sourceFilePath),
                ".pdf" => await _pdfExtractor.ExtractTextAsync(sourceFilePath, cancellationToken).ConfigureAwait(false),
                _ => string.Empty,
            };
        }
        catch (Exception ex)
        {
            return ImportResult.Fail("Could not extract text: " + ex.Message);
        }

        if (extension is not ".txt" and not ".md" and not ".docx" and not ".pdf")
        {
            return ImportResult.Fail("Unsupported file type: " + extension);
        }

        var now = DateTimeOffset.UtcNow;
        var prompt = new PromptRecord(
            Guid.NewGuid(),
            string.IsNullOrWhiteSpace(title) ? "Imported prompt" : title,
            body,
            BodyFormat.PlainText,
            categoryId,
            subCategoryId,
            null,
            false,
            false,
            Array.Empty<string>(),
            now,
            now);

        try
        {
            var created = await _store.CreateAsync(prompt, cancellationToken).ConfigureAwait(false);
            // Emit after the store call reports success, so a failed create
            // does not look like a successful import in the event stream.
            _events.Emit(PromptEventNames.Imported, created.Id);
            return ImportResult.Ok(created);
        }
        catch (Exception ex)
        {
            return ImportResult.Fail("Could not save the imported prompt: " + ex.Message);
        }
    }

    private static string ExtractDocxText(string path)
    {
        using var document = WordprocessingDocument.Open(path, isEditable: false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null) return string.Empty;
        var sb = new StringBuilder();
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            sb.AppendLine(paragraph.InnerText);
        }
        return sb.ToString();
    }
}