// ============================================================================
// filepath: src/DeploymentVault.Platform/Export/IVaultExporter.cs
// layer:    Infrastructure
// module:   DeploymentVault.Platform.Export
// since:    Phase B.2 (Deployment Vault export)
// purpose:  Contract for exporting Deployment Vault objects to files. The
//           implementation reuses FileHarvester.Core's IFormatWriterRegistry;
//           the vault never writes its own format writers.
//
// RELATIONSHIPS
//   Implements : n/a (interface)
//   Depends on : DeploymentVault.Core.Domain.InfrastructureRecord
//   Used by    : DeploymentVaultViewModel (Level 3 record export),
//                VaultExporter
//   See also   : VaultExporter, VaultExportResult, VaultDocumentAdapter
//
// NOTES
//   - Level 3 (single record) ships first. ExportApplicationAsync and
//     ExportAllAsync are added in B.4/B.5 when the ZIP builder lands.
//   - The caller owns the save picker: outputPath is the full path the user
//     chose. The exporter writes atomically (temp file + move) and never
//     prompts.
// ============================================================================

using DeploymentVault.Core.Domain;

namespace DeploymentVault.Platform.Export;

/// <summary>
/// One application with all of its records, as passed to the whole-vault
/// export. The caller (ViewModel) loads the data through
/// IDeploymentVaultStore; the exporter never touches the database.
/// </summary>
public sealed record VaultApplicationExport(
    Application Application,
    IReadOnlyList<InfrastructureRecord> Records);

/// <summary>
/// Exports Deployment Vault objects to user-chosen file paths.
/// </summary>
public interface IVaultExporter
{
    /// <summary>
    /// Exports one infrastructure record in the requested format.
    /// </summary>
    /// <param name="record">The record to export. Never null.</param>
    /// <param name="extension">
    /// The target format extension including the leading dot (".md", ".txt",
    /// ".json", ".csv", ".pdf", ".zip"). Case-insensitive.
    /// </param>
    /// <param name="outputPath">
    /// The full destination path chosen by the user. The exporter overwrites
    /// an existing file at this path.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The export result. Never null.</returns>
    Task<VaultExportResult> ExportRecordAsync(
        InfrastructureRecord record,
        string extension,
        string outputPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports one application and all of its records as a ZIP archive with
    /// the folder structure: application.md, records/&lt;record&gt;/record.md
    /// + record.txt, records.csv.
    /// </summary>
    /// <param name="application">The application to export. Never null.</param>
    /// <param name="records">All records of the application. Never null.</param>
    /// <param name="outputPath">The full destination path chosen by the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The export result. Never null.</returns>
    Task<VaultExportResult> ExportApplicationAsync(
        Application application,
        IReadOnlyList<InfrastructureRecord> records,
        string outputPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports the whole vault as a ZIP archive: applications/&lt;app&gt;/...
    /// folders, summary.csv, records.csv, README.txt.
    /// </summary>
    /// <param name="applications">Every application with its records. Never null.</param>
    /// <param name="outputPath">The full destination path chosen by the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The export result. Never null.</returns>
    Task<VaultExportResult> ExportAllAsync(
        IReadOnlyList<VaultApplicationExport> applications,
        string outputPath,
        CancellationToken cancellationToken = default);
}
