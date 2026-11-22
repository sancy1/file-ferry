// ============================================================================
// filepath: src/DeploymentVault.Platform/Export/VaultExporter.cs
// layer:    Infrastructure
// module:   DeploymentVault.Platform.Export
// since:    Phase B.2 (Deployment Vault export)
// purpose:  Orchestrates vault exports: adapts the domain object via
//           VaultDocumentAdapter, resolves the writer via
//           IFormatWriterRegistry, writes atomically (temp file + move).
//
// RELATIONSHIPS
//   Implements : IVaultExporter
//   Depends on : IFormatWriterRegistry, VaultDocumentAdapter,
//                DeploymentVault.Core.Domain.InfrastructureRecord
//   Used by    : DeploymentVaultViewModel
//   See also   : PromptExporter (the pattern this copies, minus resilience)
//
// NOTES
//   - No Portfolio.Resilience (F-014): failures throw/return directly.
//   - No overwrite confirmer: the save picker already chose the path.
//   - .json and .csv exports use IncludeHeaders = true so the output carries
//     the writer's structured envelope (JSON payload / CSV header+row)
//     instead of a bare content string. .md/.txt/.pdf stay header-free.
//   - The temp file is always deleted in finally; a failed write never
//     leaves a partial file at the destination.
// ============================================================================

using System.IO.Compression;
using System.Text;
using DeploymentVault.Core.Domain;
using FileHarvester.Core.Domain;
using FileHarvester.Core.Writers;

namespace DeploymentVault.Platform.Export;

/// <summary>
/// Exports Deployment Vault objects using the harvester's format writers.
/// </summary>
public sealed class VaultExporter : IVaultExporter
{
    private readonly IFormatWriterRegistry _writers;

    /// <summary>Creates the exporter.</summary>
    /// <param name="writers">The format writer registry. Never null.</param>
    public VaultExporter(IFormatWriterRegistry writers)
    {
        ArgumentNullException.ThrowIfNull(writers);
        _writers = writers;
    }

    /// <inheritdoc/>
    public async Task<VaultExportResult> ExportRecordAsync(
        InfrastructureRecord record,
        string extension,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var writer = _writers.GetByExtension(extension);
        if (writer is null)
        {
            return VaultExportResult.Fail("No format writer registered for extension '" + extension + "'.");
        }

        var document = VaultDocumentAdapter.ToComposedDocument(record, writer);
        if (writer.Extension is ".json" or ".csv")
        {
            document = document with { IncludeHeaders = true };
        }

        var tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

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
            return VaultExportResult.Ok(outputPath);
        }
        catch (Exception ex)
        {
            return VaultExportResult.Fail(ex.Message);
        }
        finally
        {
            TryDeleteTemp(tempPath);
        }
    }

    /// <inheritdoc/>
    public async Task<VaultExportResult> ExportApplicationAsync(
        Application application,
        IReadOnlyList<InfrastructureRecord> records,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var markdown = RequireWriter(".md");
        var text = RequireWriter(".txt");
        if (markdown is null || text is null)
        {
            return VaultExportResult.Fail("Markdown or text writer is not registered.");
        }

        return await WriteZipAsync(outputPath, async archive =>
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await AddDocumentEntryAsync(
                archive, used, "application.md", markdown,
                VaultDocumentAdapter.ToComposedDocument(application, markdown),
                cancellationToken).ConfigureAwait(false);

            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var folder = "records/" + UniqueName(used, SanitizeName(record.Name), record.Id);
                await AddDocumentEntryAsync(
                    archive, used, folder + "/record.md", markdown,
                    VaultDocumentAdapter.ToComposedDocument(record, markdown),
                    cancellationToken).ConfigureAwait(false);
                await AddDocumentEntryAsync(
                    archive, used, folder + "/record.txt", text,
                    VaultDocumentAdapter.ToComposedDocument(record, text),
                    cancellationToken).ConfigureAwait(false);
            }

            AddTextEntry(archive, used, "records.csv", BuildRecordsCsv(records, application.Name));
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<VaultExportResult> ExportAllAsync(
        IReadOnlyList<VaultApplicationExport> applications,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applications);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var markdown = RequireWriter(".md");
        var text = RequireWriter(".txt");
        if (markdown is null || text is null)
        {
            return VaultExportResult.Fail("Markdown or text writer is not registered.");
        }

        return await WriteZipAsync(outputPath, async archive =>
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allRecords = new List<(InfrastructureRecord Record, string ApplicationName)>();

            foreach (var item in applications)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var appFolder = "applications/" + UniqueName(used, SanitizeName(item.Application.Name), item.Application.Id);

                await AddDocumentEntryAsync(
                    archive, used, appFolder + "/application.md", markdown,
                    VaultDocumentAdapter.ToComposedDocument(item.Application, markdown),
                    cancellationToken).ConfigureAwait(false);

                foreach (var record in item.Records)
                {
                    var folder = appFolder + "/records/" + UniqueName(used, SanitizeName(record.Name), record.Id);
                    await AddDocumentEntryAsync(
                        archive, used, folder + "/record.md", markdown,
                        VaultDocumentAdapter.ToComposedDocument(record, markdown),
                        cancellationToken).ConfigureAwait(false);
                    await AddDocumentEntryAsync(
                        archive, used, folder + "/record.txt", text,
                        VaultDocumentAdapter.ToComposedDocument(record, text),
                        cancellationToken).ConfigureAwait(false);
                    allRecords.Add((record, item.Application.Name));
                }

                AddTextEntry(archive, used, appFolder + "/records.csv", BuildRecordsCsv(item.Records, item.Application.Name));
            }

            AddTextEntry(archive, used, "summary.csv", BuildSummaryCsv(applications));
            AddTextEntry(archive, used, "records.csv", BuildAllRecordsCsv(allRecords));
            AddTextEntry(archive, used, "README.txt", BuildReadme(applications.Count, allRecords.Count));
        }, cancellationToken).ConfigureAwait(false);
    }

    // ---- ZIP helpers ----

    private IFormatWriter? RequireWriter(string extension) => _writers.GetByExtension(extension);

    private static async Task<VaultExportResult> WriteZipAsync(
        string outputPath,
        Func<ZipArchive, Task> populate,
        CancellationToken cancellationToken)
    {
        var tempPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using (var stream = new FileStream(
                path: tempPath,
                mode: FileMode.Create,
                access: FileAccess.Write,
                share: FileShare.None,
                bufferSize: 64 * 1024,
                useAsync: true))
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    await populate(archive).ConfigureAwait(false);
                }
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, outputPath, overwrite: true);
            return VaultExportResult.Ok(outputPath);
        }
        catch (Exception ex)
        {
            return VaultExportResult.Fail(ex.Message);
        }
        finally
        {
            TryDeleteTemp(tempPath);
        }
    }

    private static async Task AddDocumentEntryAsync(
        ZipArchive archive,
        HashSet<string> used,
        string entryName,
        IFormatWriter writer,
        ComposedDocument document,
        CancellationToken cancellationToken)
    {
        byte[] bytes;
        await using (var mem = new MemoryStream())
        {
            await writer.WriteAsync(document, mem, cancellationToken).ConfigureAwait(false);
            bytes = mem.ToArray();
        }

        var entry = archive.CreateEntry(UniqueEntryName(used, entryName), CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await entryStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static void AddTextEntry(
        ZipArchive archive,
        HashSet<string> used,
        string entryName,
        string text)
    {
        var entry = archive.CreateEntry(UniqueEntryName(used, entryName), CompressionLevel.Optimal);
        using var stream = entry.Open();
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string UniqueEntryName(HashSet<string> used, string entryName)
    {
        var name = entryName.Replace('\\', '/');
        if (used.Add(name)) return name;
        var dot = name.LastIndexOf('.');
        var stem = dot > 0 ? name[..dot] : name;
        var ext = dot > 0 ? name[dot..] : string.Empty;
        var i = 2;
        string candidate;
        do
        {
            candidate = $"{stem}-{i}{ext}";
            i++;
        }
        while (!used.Add(candidate));
        return candidate;
    }

    private static string UniqueName(HashSet<string> used, string baseName, Guid id)
    {
        var candidate = baseName;
        if (!used.Add(candidate))
        {
            candidate = baseName + "-" + id.ToString("N")[..8];
            used.Add(candidate);
        }
        return candidate;
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }
        var result = sb.ToString().Trim();
        return result.Length == 0 ? "Untitled" : result;
    }

    // ---- CSV builders ----

    private static string BuildRecordsCsv(IReadOnlyList<InfrastructureRecord> records, string applicationName)
    {
        var sb = new StringBuilder();
        sb.Append("Application,Name,Provider,Category,Environment,Status,DashboardUrl,LoginEmail,Region,ResourceId,Tags,CreatedAtUtc,UpdatedAtUtc,DeletedAtUtc\r\n");
        foreach (var r in records)
        {
            sb.Append(Csv(applicationName)).Append(',')
              .Append(Csv(r.Name)).Append(',')
              .Append(Csv(r.Provider)).Append(',')
              .Append(Csv(r.ServiceCategory.ToString())).Append(',')
              .Append(Csv(r.Environment.ToString())).Append(',')
              .Append(Csv(r.Status.ToString())).Append(',')
              .Append(Csv(r.DashboardUrl)).Append(',')
              .Append(Csv(r.LoginEmail)).Append(',')
              .Append(Csv(r.Region)).Append(',')
              .Append(Csv(r.ResourceId)).Append(',')
              .Append(Csv(string.Join("; ", r.Tags))).Append(',')
              .Append(Csv(r.CreatedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture))).Append(',')
              .Append(Csv(r.UpdatedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture))).Append(',')
              .Append(Csv(r.DeletedAtUtc?.ToString("O", System.Globalization.CultureInfo.InvariantCulture)))
              .Append("\r\n");
        }
        return sb.ToString();
    }

    private static string BuildAllRecordsCsv(IReadOnlyList<(InfrastructureRecord Record, string ApplicationName)> records)
    {
        var sb = new StringBuilder();
        sb.Append("Application,Name,Provider,Category,Environment,Status,DashboardUrl,LoginEmail,Region,ResourceId,Tags,CreatedAtUtc,UpdatedAtUtc,DeletedAtUtc\r\n");
        foreach (var (r, appName) in records)
        {
            sb.Append(Csv(appName)).Append(',')
              .Append(Csv(r.Name)).Append(',')
              .Append(Csv(r.Provider)).Append(',')
              .Append(Csv(r.ServiceCategory.ToString())).Append(',')
              .Append(Csv(r.Environment.ToString())).Append(',')
              .Append(Csv(r.Status.ToString())).Append(',')
              .Append(Csv(r.DashboardUrl)).Append(',')
              .Append(Csv(r.LoginEmail)).Append(',')
              .Append(Csv(r.Region)).Append(',')
              .Append(Csv(r.ResourceId)).Append(',')
              .Append(Csv(string.Join("; ", r.Tags))).Append(',')
              .Append(Csv(r.CreatedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture))).Append(',')
              .Append(Csv(r.UpdatedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture))).Append(',')
              .Append(Csv(r.DeletedAtUtc?.ToString("O", System.Globalization.CultureInfo.InvariantCulture)))
              .Append("\r\n");
        }
        return sb.ToString();
    }

    private static string BuildSummaryCsv(IReadOnlyList<VaultApplicationExport> applications)
    {
        var sb = new StringBuilder();
        sb.Append("Name,Status,Owner,RepositoryUrl,ApplicationUrl,Tags,RecordCount,CreatedAtUtc,UpdatedAtUtc,DeletedAtUtc\r\n");
        foreach (var item in applications)
        {
            var a = item.Application;
            sb.Append(Csv(a.Name)).Append(',')
              .Append(Csv(a.Status.ToString())).Append(',')
              .Append(Csv(a.Owner)).Append(',')
              .Append(Csv(a.RepositoryUrl)).Append(',')
              .Append(Csv(a.ApplicationUrl)).Append(',')
              .Append(Csv(string.Join("; ", a.Tags))).Append(',')
              .Append(item.Records.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(Csv(a.CreatedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture))).Append(',')
              .Append(Csv(a.UpdatedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture))).Append(',')
              .Append(Csv(a.DeletedAtUtc?.ToString("O", System.Globalization.CultureInfo.InvariantCulture)))
              .Append("\r\n");
        }
        return sb.ToString();
    }

    private static string BuildReadme(int applicationCount, int recordCount)
    {
        var sb = new StringBuilder();
        sb.Append("Deployment Vault export\r\n");
        sb.Append("========================\r\n\r\n");
        sb.Append("This archive contains ").Append(applicationCount).Append(" application(s) and ")
          .Append(recordCount).Append(" infrastructure record(s).\r\n\r\n");
        sb.Append("Structure:\r\n");
        sb.Append("  applications/<application>/application.md   - application metadata\r\n");
        sb.Append("  applications/<application>/records/<record>/record.md - record metadata (Markdown)\r\n");
        sb.Append("  applications/<application>/records/<record>/record.txt - record metadata (plain text)\r\n");
        sb.Append("  applications/<application>/records.csv      - this application's records\r\n");
        sb.Append("  summary.csv                                 - one row per application\r\n");
        sb.Append("  records.csv                                 - one row per record across all applications\r\n\r\n");
        sb.Append("Secret values are never included in exports.\r\n");
        return sb.ToString();
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.AsSpan().IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
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
}
