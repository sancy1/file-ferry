// ============================================================================
// filepath: src/DeploymentVault.Core/EnvSafe/Abstractions/IEnvFileReader.cs
// layer:    Abstractions
// module:   DeploymentVault.Core.EnvSafe.Abstractions
// since:    Phase A (EnvSafe)
// purpose:  Contract for reading an imported source file (.env, .txt, .md,
//           .docx) into plain text for the EnvSafe converter.
//
// RELATIONSHIPS
//   Implements : n/a
//   Depends on : n/a
//   Used by    : EnvFileReader, EnvSafeViewModel
//   See also   : IEnvFileValidator, IEnvFileConverter
//
// NOTES
//   - Stateless. The same instance is safe to share.
//   - .env/.txt/.md are read as UTF-8 text; .docx is parsed to plain text
//     via DocumentFormat.OpenXml (paragraphs joined with newlines).
// ============================================================================

namespace DeploymentVault.Core.EnvSafe.Abstractions;

/// <summary>
/// Reads an imported source file into plain text.
/// </summary>
public interface IEnvFileReader
{
    /// <summary>
    /// Reads the file at the given path and returns its plain-text content.
    /// </summary>
    /// <param name="path">The absolute file path. Never null.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The plain-text content. Never null.</returns>
    Task<string> ReadAsync(string path, CancellationToken cancellationToken = default);
}
