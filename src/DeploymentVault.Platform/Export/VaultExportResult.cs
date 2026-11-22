// ============================================================================
// filepath: src/DeploymentVault.Platform/Export/VaultExportResult.cs
// layer:    Infrastructure
// module:   DeploymentVault.Platform.Export
// since:    Phase B.2 (Deployment Vault export)
// purpose:  Result of a vault export operation. Failures are values, not
//           exceptions, mirroring Prompt.Platform's ExportResult.
// ============================================================================

namespace DeploymentVault.Platform.Export;

/// <summary>
/// The result of a vault export operation.
/// </summary>
/// <param name="Success">True when the file was written.</param>
/// <param name="OutputPath">The written path on success; null on failure.</param>
/// <param name="Error">The failure reason on failure; null on success.</param>
public sealed record VaultExportResult(bool Success, string? OutputPath, string? Error)
{
    /// <summary>Creates a success result.</summary>
    public static VaultExportResult Ok(string outputPath) => new(true, outputPath, null);

    /// <summary>Creates a failure result.</summary>
    public static VaultExportResult Fail(string error) => new(false, null, error);
}
