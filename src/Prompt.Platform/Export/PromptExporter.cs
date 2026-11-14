// ============================================================================
// FILE:     src/Prompt.Platform/Export/PromptExporter.cs
// PURPOSE:  Exports a single prompt to a file in the requested format. The
//           whole export is wrapped in the prompt-export resilience policy.
//           An existing file prompts the user before being replaced. The
//           write goes to a temp path and is renamed into place on success,
//           so a failed or cancelled write never leaves a partial file.
// RELATION: Implements IPromptExporter (Prompt.Core.Domain). Consumes
//           IFormatWriterRegistry, IResilienceExecutor, IOverwriteConfirmer,
//           PromptDocumentAdapter (P3.1), and PromptPolicyNames.PromptExport.
//           Consumed by PromptExportDialogViewModel (P3.4).
// NOTES:    - The FileStream pattern mirrors OutputSink's: temp path, write,
//             flush, File.Move(overwrite:true), try/finally delete.
//           - The exporter owns the FileStream; the IFormatWriter must not
//             close it.
//           - The existence check + the user prompt run inside the
//             prompt-export policy's operation, not as separate policy calls
//             (one policy for the whole operation).
//           - ResilienceException is caught and converted to ExportResult.Fail;
//             failures are values, not exceptions to the caller.
// ============================================================================

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileHarvester.Core.Domain;
using FileHarvester.Core.Writers;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Errors;
using Prompt.Core.Domain;
using Prompt.Platform.Composition;
using Prompt.Platform.Observability;
using PromptRecord = Prompt.Core.Domain.Prompt;

namespace Prompt.Platform.Export;

/// <summary>
/// Exports a single prompt to a file. The prompt-export policy wraps the
/// whole operation. An existing file prompts the user via IOverwriteConfirmer.
/// </summary>
public sealed class PromptExporter : IPromptExporter
{
    private readonly IFormatWriterRegistry _writers;
    private readonly IResilienceExecutor _executor;
    private readonly IOverwriteConfirmer _confirmer;
    private readonly ICategoryStore _categories;
    private readonly ISubCategoryStore _subCategories;
    private readonly IPromptEventEmitter _events;

    /// <summary>Creates the exporter. All five collaborators are required.</summary>
    /// <param name="events">
    /// The operation-event emitter. When null, <see cref="NullPromptEventEmitter.Instance"/>
    /// is used so the exporter can emit unconditionally without null checks.
    /// </param>
    public PromptExporter(
        IFormatWriterRegistry writers,
        IResilienceExecutor executor,
        IOverwriteConfirmer confirmer,
        ICategoryStore categories,
        ISubCategoryStore subCategories,
        IPromptEventEmitter? events = null)
    {
        _writers = writers ?? throw new ArgumentNullException(nameof(writers));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _confirmer = confirmer ?? throw new ArgumentNullException(nameof(confirmer));
        _categories = categories ?? throw new ArgumentNullException(nameof(categories));
        _subCategories = subCategories ?? throw new ArgumentNullException(nameof(subCategories));
        _events = events ?? NullPromptEventEmitter.Instance;
    }

    /// <inheritdoc/>
    public async Task<ExportResult> ExportSingleAsync(
        PromptRecord prompt,
        string extension,
        string destinationFolder,
        string? outputBaseName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFolder);

        var writer = _writers.GetByExtension(extension);
        if (writer is null)
        {
            return ExportResult.Fail("No format writer registered for extension '" + extension + "'.");
        }

        try
        {
            Directory.CreateDirectory(destinationFolder);
        }
        catch (Exception ex)
        {
            return ExportResult.Fail("Cannot create destination folder: " + ex.Message);
        }

        try
        {
            var result = await _executor.ExecuteAsync(
                PromptPolicyNames.PromptExport,
                async ct => await ExportCoreAsync(prompt, writer, destinationFolder, outputBaseName, ct).ConfigureAwait(false),
                fallback: null,
                idempotencyKey: null,
                timeBudgetMs: null,
                ct: cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                _events.Emit(PromptEventNames.Exported, prompt.Id);
            }
            return result;
        }
        catch (ResilienceException ex)
        {
            return ExportResult.Fail(ex.Category + ": " + (ex.InnerException?.Message ?? ex.Message));
        }
    }

    private async Task<ExportResult> ExportCoreAsync(
        PromptRecord prompt,
        IFormatWriter writer,
        string destinationFolder,
        string? outputBaseName,
        CancellationToken cancellationToken)
    {
        var fileName = (string.IsNullOrWhiteSpace(outputBaseName) ? SanitizeFileName(prompt.Title) : outputBaseName) + writer.Extension;
        var outputPath = Path.Combine(destinationFolder, fileName);

        if (File.Exists(outputPath))
        {
            var decision = await _confirmer.ConfirmAsync(outputPath, cancellationToken).ConfigureAwait(false);
            if (decision.Decision != OverwriteDecision.Replace)
            {
                return ExportResult.Fail("Cancelled by user: file exists");
            }
        }

        var document = PromptDocumentAdapter.ToComposedDocument(prompt, writer);

        var tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                path: tempPath,
                mode: FileMode.Create,
                access: FileAccess.Write,
                share: FileShare.None,
                bufferSize: 64 * 1024,
                useAsync: true))
            {
                await writer.WriteAsync(document, stream, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(tempPath, outputPath, overwrite: true);
        }
        finally
        {
            TryDeleteTemp(tempPath);
        }

        return ExportResult.Ok(outputPath, fileCount: 1);
    }

    private static string SanitizeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(title.Length);
        foreach (var c in title)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }
        var result = sb.ToString().Trim();
        return result.Length == 0 ? "Untitled" : result;
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath)) { File.Delete(tempPath); }
        }
        catch
        {
            // Best-effort cleanup; must not mask the primary error.
        }
    }

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <inheritdoc/>
    public async Task<ExportResult> ExportManyAsync(
        IReadOnlyList<PromptRecord> prompts,
        string extension,
        string destinationFolder,
        string? zipBaseName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFolder);

        var writer = _writers.GetByExtension(extension);
        if (writer is null)
        {
            return ExportResult.Fail("No format writer registered for extension '" + extension + "'.");
        }

        try
        {
            Directory.CreateDirectory(destinationFolder);
        }
        catch (Exception ex)
        {
            return ExportResult.Fail("Cannot create destination folder: " + ex.Message);
        }

        try
        {
            var result = await _executor.ExecuteAsync(
                PromptPolicyNames.PromptExport,
                async ct => await ExportManyCoreAsync(prompts, writer, destinationFolder, zipBaseName, ct).ConfigureAwait(false),
                fallback: null,
                idempotencyKey: null,
                timeBudgetMs: null,
                ct: cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                // Bulk export: no single prompt id.
                _events.Emit(PromptEventNames.Exported);
            }
            return result;
        }
        catch (ResilienceException ex)
        {
            return ExportResult.Fail(ex.Category + ": " + (ex.InnerException?.Message ?? ex.Message));
        }
    }

    private async Task<ExportResult> ExportManyCoreAsync(
        IReadOnlyList<PromptRecord> prompts,
        IFormatWriter writer,
        string destinationFolder,
        string? zipBaseName,
        CancellationToken cancellationToken)
    {
        // Category and sub-category name lookups, one call each.
        var categories = await _categories.ListAsync(cancellationToken).ConfigureAwait(false);
        var subCategories = await _subCategories.ListAsync(null, cancellationToken).ConfigureAwait(false);
        var categoryNames = categories.ToDictionary(c => c.Id, c => c.Name);
        var subCategoryNames = subCategories.ToDictionary(s => s.Id, s => s.Name);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var zipName = (string.IsNullOrWhiteSpace(zipBaseName) ? "PromptExport" : zipBaseName) + "-" + stamp + ".zip";
        var outputPath = Path.Combine(destinationFolder, zipName);
        var tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");

        var manifestEntries = new List<ManifestEntry>();
        var index = 1;
        long totalBytes = 0;

        try
        {
            await using (var fileStream = new FileStream(
                path: tempPath,
                mode: FileMode.Create,
                access: FileAccess.Write,
                share: FileShare.None,
                bufferSize: 64 * 1024,
                useAsync: true))
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var prompt in prompts)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var categoryName = prompt.CategoryId.HasValue && categoryNames.TryGetValue(prompt.CategoryId.Value, out var cn)
                        ? cn
                        : "Uncategorized";
                    var subCategoryName = prompt.SubCategoryId.HasValue && subCategoryNames.TryGetValue(prompt.SubCategoryId.Value, out var sn)
                        ? sn
                        : null;

                    var sanitizedCategory = SanitizeFileName(categoryName);
                    var sanitizedSub = subCategoryName is null ? null : SanitizeFileName(subCategoryName);
                    var sanitizedTitle = SanitizeFileName(prompt.Title);
                    var entryName = sanitizedSub is null
                        ? sanitizedCategory + "/" + sanitizedTitle + writer.Extension
                        : sanitizedCategory + "/" + sanitizedSub + "/" + sanitizedTitle + writer.Extension;

                    var composed = PromptDocumentAdapter.ToComposedDocument(prompt, writer);

                    using var memStream = new MemoryStream();
                    await writer.WriteAsync(composed, memStream, cancellationToken).ConfigureAwait(false);
                    totalBytes += memStream.Length;

                    var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                    await using (var entryStream = entry.Open())
                    {
                        memStream.Position = 0;
                        await memStream.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
                    }

                    manifestEntries.Add(new ManifestEntry
                    {
                        Index = index,
                        OriginalPath = prompt.Title,
                        ArchiveName = entryName,
                        SizeBytes = memStream.Length,
                        Sha256 = composed.Files[0].Sha256,
                        Encoding = "utf-8",
                        IsBinary = false,
                        IsUnreadable = false,
                        ReadError = null,
                    });
                    index++;
                }

                var manifest = new Manifest
                {
                    GeneratedAtUtc = DateTime.UtcNow,
                    FileCount = prompts.Count,
                    TotalBytes = totalBytes,
                    Entries = manifestEntries,
                };
                var manifestJson = JsonSerializer.Serialize(manifest, ManifestJsonOptions);

                var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                await using (var manifestStream = manifestEntry.Open())
                {
                    var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(manifestJson);
                    await manifestStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                }
            }

            File.Move(tempPath, outputPath, overwrite: true);
        }
        finally
        {
            TryDeleteTemp(tempPath);
        }

        return ExportResult.Ok(outputPath, prompts.Count);
    }

    private sealed class Manifest
    {
        [JsonPropertyName("generatedAtUtc")] public DateTime GeneratedAtUtc { get; set; }
        [JsonPropertyName("fileCount")] public int FileCount { get; set; }
        [JsonPropertyName("totalBytes")] public long TotalBytes { get; set; }
        [JsonPropertyName("entries")] public List<ManifestEntry> Entries { get; set; } = new();
    }

    private sealed class ManifestEntry
    {
        [JsonPropertyName("index")] public int Index { get; set; }
        [JsonPropertyName("originalPath")] public string OriginalPath { get; set; } = string.Empty;
        [JsonPropertyName("archiveName")] public string? ArchiveName { get; set; }
        [JsonPropertyName("sizeBytes")] public long SizeBytes { get; set; }
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = string.Empty;
        [JsonPropertyName("encoding")] public string Encoding { get; set; } = string.Empty;
        [JsonPropertyName("isBinary")] public bool IsBinary { get; set; }
        [JsonPropertyName("isUnreadable")] public bool IsUnreadable { get; set; }
        [JsonPropertyName("readError")] public string? ReadError { get; set; }
    }
}