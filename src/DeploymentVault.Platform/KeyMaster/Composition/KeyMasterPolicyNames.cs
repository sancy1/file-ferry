// ============================================================================
// filepath: src/DeploymentVault.Platform/KeyMaster/Composition/KeyMasterPolicyNames.cs
// layer:    Composition
// module:   DeploymentVault.Platform.KeyMaster.Composition
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  Resilience policy names owned by KeyMaster. Constants prevent
//           typo-driven policy misses (the documented Portfolio.Resilience
//           0.8.0 trap). Mirrors ImageTextifyPolicyNames / CodeViewerPolicyNames.
// RELATIONSHIPS
//   Used by    : KeyGeneratorRegistry, KeyMasterComposition
//   See also   : KeyMasterComposition.RegisterResiliencePolicies
// ============================================================================

namespace DeploymentVault.Platform.KeyMaster.Composition;

/// <summary>
/// Resilience policy names owned by KeyMaster.
/// </summary>
public static class KeyMasterPolicyNames
{
    /// <summary>
    /// Wraps every key generation call. Generation is local CPU work — the
    /// policy provides timeout and circuit-breaker protection against a
    /// pathological generator, not network retry.
    /// </summary>
    public const string Generate = "keymaster-generate";
}
