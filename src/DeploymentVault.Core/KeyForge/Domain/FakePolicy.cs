// ============================================================================
// filepath: src/DeploymentVault.Core/KeyForge/Domain/FakePolicy.cs
// layer:    Core / Domain
// module:   DeploymentVault.Core.KeyForge.Domain
// since:    Phase A (KeyForge)
// purpose:  Selects how fakes are generated: deterministic (same input ->
//           same fake, seeded from the original's hash) or random (new fake
//           each run). Deterministic is the default for reproducibility.
// RELATIONSHIPS
//   Used by    : IKeyForger.Forge, KeyForgeViewModel
//   See also   : ShapePreservingKeyForger
// ============================================================================

namespace DeploymentVault.Core.KeyForge.Domain;

/// <summary>
/// How fake keys are generated.
/// </summary>
public enum FakePolicy
{
    /// <summary>Seeded from the original's SHA256 hash; same input -> same fake.</summary>
    Deterministic,

    /// <summary>Unseeded random; each run produces a different fake.</summary>
    Random,
}
