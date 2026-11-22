// ============================================================================
// filepath: src/DeploymentVault.Platform/Export/VaultDocumentAdapter.cs
// layer:    Infrastructure
// module:   DeploymentVault.Platform.Export
// since:    Phase B.1 (Deployment Vault export)
// purpose:  Adapts a Deployment Vault Application or InfrastructureRecord to
//           the harvester's ComposedDocument for a given IFormatWriter. The
//           vault object is rendered to a canonical text body (one field per
//           line); the ComposedDocument is SingleMerged with headers OFF.
//
// RELATIONSHIPS
//   Implements : n/a (static adapter)
//   Depends on : FileHarvester.Core.Domain (ComposedDocument, FileContent,
//                ResolvedFile, FileRequest, ResolutionConfidence),
//                FileHarvester.Core.Writers (IFormatWriter),
//                DeploymentVault.Core.Domain (Application, InfrastructureRecord)
//   Used by    : VaultExporter (B.2+), which invokes IFormatWriter.WriteAsync
//                with the result.
//   See also   : PromptDocumentAdapter (the pattern this copies)
//
// NOTES
//   - The FileRequest/ResolvedFile/FileContent are synthetic: the vault
//     object is not a file on disk. The AbsolutePath is the sanitized name +
//     the writer's extension; the confidence is Exact (the body is in memory).
//   - The SHA-256 is the body's UTF-8 bytes, lowercase hex.
//   - Redaction: Phase B ships redacted-only, and no secrets exist yet
//     (secrets table is Phase B.1+). When secrets land, the body renderer
//     must omit values by default; this adapter is the single place that
//     decides what text reaches the writers.
//   - The body is format-neutral text. Format-specific shaping (CSV header
//     row, JSON envelope) is the exporter's job, not the adapter's.
// ============================================================================

using System.Security.Cryptography;
using System.Text;
using DeploymentVault.Core.Domain;
using FileHarvester.Core.Domain;
using FileHarvester.Core.Writers;

namespace DeploymentVault.Platform.Export;

/// <summary>
/// Adapts a Deployment Vault object to a ComposedDocument for the given
/// IFormatWriter.
/// </summary>
public static class VaultDocumentAdapter
{
    /// <summary>
    /// Builds the ComposedDocument for a single application.
    /// </summary>
    /// <param name="application">The application to adapt. Never null.</param>
    /// <param name="writer">The format writer. Never null.</param>
    /// <returns>The ComposedDocument.</returns>
    public static ComposedDocument ToComposedDocument(Application application, IFormatWriter writer)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(writer);

        var body = RenderApplicationBody(application);
        return ToDocument(body, application.Name, application.UpdatedAtUtc, writer);
    }

    /// <summary>
    /// Builds the ComposedDocument for a single infrastructure record.
    /// </summary>
    /// <param name="record">The record to adapt. Never null.</param>
    /// <param name="writer">The format writer. Never null.</param>
    /// <returns>The ComposedDocument.</returns>
    public static ComposedDocument ToComposedDocument(InfrastructureRecord record, IFormatWriter writer)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(writer);

        var body = RenderRecordBody(record);
        return ToDocument(body, record.Name, record.UpdatedAtUtc, writer);
    }

    private static ComposedDocument ToDocument(
        string body, string name, DateTimeOffset updatedAtUtc, IFormatWriter writer)
    {
        var request = new FileRequest(0, name);
        var fileName = SanitizeFileName(name) + writer.Extension;

        var rawBytes = Encoding.UTF8.GetBytes(body);
        var sha256 = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();

        var resolvedFile = new ResolvedFile(
            Request: request,
            AbsolutePath: fileName,
            SizeBytes: rawBytes.Length,
            LastModifiedUtc: updatedAtUtc.UtcDateTime,
            Confidence: ResolutionConfidence.Exact);

        var fileContent = new FileContent(
            File: resolvedFile,
            RawBytes: rawBytes,
            DecodedText: body,
            Encoding: Encoding.UTF8,
            IsBinary: false,
            Sha256: sha256);

        return ComposedDocument.ForSingleMerged(new[] { fileContent }) with
        {
            IncludeHeaders = false,
            IncludeCodeFences = false,
        };
    }

    private static string RenderApplicationBody(Application app)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(app.Name).Append('\n');
        AppendLine(sb, "Status", app.Status.ToString());
        AppendLine(sb, "Owner", app.Owner);
        AppendLine(sb, "Description", app.Description);
        AppendLine(sb, "Repository", app.RepositoryUrl);
        AppendLine(sb, "Application URL", app.ApplicationUrl);
        AppendLine(sb, "Tags", app.Tags.Count == 0 ? null : string.Join(", ", app.Tags));
        AppendLine(sb, "Notes", app.Notes);
        AppendLine(sb, "Created (UTC)", app.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
        AppendLine(sb, "Updated (UTC)", app.UpdatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private static string RenderRecordBody(InfrastructureRecord record)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(record.Name).Append('\n');
        AppendLine(sb, "Provider", record.Provider);
        AppendLine(sb, "Category", record.ServiceCategory.ToString());
        AppendLine(sb, "Environment", record.Environment.ToString());
        AppendLine(sb, "Status", record.Status.ToString());
        AppendLine(sb, "Dashboard URL", record.DashboardUrl);
        AppendLine(sb, "Login Email", record.LoginEmail);
        AppendLine(sb, "Region", record.Region);
        AppendLine(sb, "Resource ID", record.ResourceId);
        AppendLine(sb, "Tags", record.Tags.Count == 0 ? null : string.Join(", ", record.Tags));
        AppendLine(sb, "Notes", record.Notes);
        AppendLine(sb, "Created (UTC)", record.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
        AppendLine(sb, "Updated (UTC)", record.UpdatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private static void AppendLine(StringBuilder sb, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.Append("- **").Append(label).Append(":** ").Append(value).Append('\n');
    }

    private static string SanitizeFileName(string name)
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
}
