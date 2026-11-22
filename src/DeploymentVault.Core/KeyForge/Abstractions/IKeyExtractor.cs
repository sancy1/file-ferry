// ============================================================================
// filepath: src/DeploymentVault.Core/KeyForge/Abstractions/IKeyExtractor.cs
// layer:    Core / Abstractions
// module:   DeploymentVault.Core.KeyForge.Abstractions
// since:    Phase A (KeyForge)
// purpose:  Contract for locating key-shaped spans in plain text. The
//           extractor returns spans only; forgery is a separate concern.
// RELATIONSHIPS
//   Implemented by : RegexKeyExtractor (DeploymentVault.Platform.KeyForge)
//   Used by        : KeyForgeViewModel
//   See also       : IKeyForger
// ============================================================================

using DeploymentVault.Core.KeyForge.Domain;

namespace DeploymentVault.Core.KeyForge.Abstractions;

/// <summary>
/// Locates key-shaped spans in plain text.
/// </summary>
public interface IKeyExtractor
{
    /// <summary>
    /// Finds every key-shaped span in <paramref name="text"/>.
    /// </summary>
    /// <param name="text">The source text. Not null.</param>
    /// <returns>The detected spans, in order of appearance. Empty when none.</returns>
    IReadOnlyList<KeySpan> Extract(string text);
}
