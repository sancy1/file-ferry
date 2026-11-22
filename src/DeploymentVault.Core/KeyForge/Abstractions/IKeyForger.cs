// ============================================================================
// filepath: src/DeploymentVault.Core/KeyForge/Abstractions/IKeyForger.cs
// layer:    Core / Abstractions
// module:   DeploymentVault.Core.KeyForge.Abstractions
// since:    Phase A (KeyForge)
// purpose:  Contract for producing a shape-identical fake for a single key.
//           The fake preserves prefix, character classes, length, separators,
//           and structure; only the characters differ.
// RELATIONSHIPS
//   Implemented by : ShapePreservingKeyForger (DeploymentVault.Platform.KeyForge)
//   Used by        : KeyForgeViewModel
//   See also       : IKeyExtractor
// ============================================================================

using DeploymentVault.Core.KeyForge.Domain;

namespace DeploymentVault.Core.KeyForge.Abstractions;

/// <summary>
/// Produces shape-identical fake keys.
/// </summary>
public interface IKeyForger
{
    /// <summary>
    /// Forges a fake for <paramref name="original"/> matching its family shape.
    /// </summary>
    /// <param name="original">The real key value. Not null.</param>
    /// <param name="family">The detected key family.</param>
    /// <param name="policy">Deterministic (default) or random.</param>
    /// <returns>A fake of identical shape. Never the original.</returns>
    string Forge(string original, KeyFamily family, FakePolicy policy);
}
