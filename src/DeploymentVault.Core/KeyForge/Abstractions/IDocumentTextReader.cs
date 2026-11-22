// ============================================================================
// filepath: src/DeploymentVault.Core/KeyForge/Abstractions/IDocumentTextReader.cs
// layer:    Core / Abstractions
// module:   DeploymentVault.Core.KeyForge.Abstractions
// since:    Phase A (KeyForge)
// purpose:  Contract for reading an imported document (.txt, .md, .env,
//           .json, .yaml, .docx, .pdf, .xlsx) into plain text. KeyForge
//           forges keys in text form; it does not write back into the
//           original container.
// RELATIONSHIPS
//   Implemented by : KeyForgeDocumentReader (DeploymentVault.Platform.KeyForge)
//   Used by        : KeyForgeViewModel
//   See also       : IKeyExtractor
// ============================================================================

namespace DeploymentVault.Core.KeyForge.Abstractions;

/// <summary>
/// Reads an imported document into plain text.
/// </summary>
public interface IDocumentTextReader
{
    /// <summary>
    /// Reads the file at <paramref name="path"/> and returns its plain text.
    /// </summary>
    /// <param name="path">The file path. Not null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document's plain text.</returns>
    Task<string> ReadAsync(string path, CancellationToken cancellationToken = default);
}
