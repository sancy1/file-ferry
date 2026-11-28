// ============================================================================
// filepath: src/DeploymentVault.Platform/KeyMaster/Composition/KeyMasterComposition.cs
// layer:    Composition
// module:   DeploymentVault.Platform.KeyMaster.Composition
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  Registers KeyMaster-owned Portfolio.Resilience policies into the
//           ResilienceBuilder supplied by AddFileHarvester's feature-policy
//           callback. Mirrors ImageTextifyComposition / CodeViewerComposition.
// RELATIONSHIPS
//   Invoked from : App.axaml.cs via AddFileHarvester's
//                  configureDeploymentVaultPolicies parameter.
//   See also     : KeyMasterPolicyNames, KeyGeneratorRegistry
// ============================================================================

using Portfolio.Resilience.Configuration;

namespace DeploymentVault.Platform.KeyMaster.Composition;

/// <summary>
/// Composition helpers for KeyMaster-owned resilience policies.
/// </summary>
public static class KeyMasterComposition
{
    /// <summary>
    /// Registers the keymaster-generate policy.
    /// </summary>
    /// <param name="builder">The resilience builder from AddFileHarvester. Not null.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="builder"/> is null.</exception>
    public static void RegisterResiliencePolicies(ResilienceBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddPolicy(KeyMasterPolicyNames.Generate, p =>
        {
            // Generation is local CPU work: no retries (a failure is
            // deterministic, not transient), a generous timeout so RSA-4096
            // never trips it, and a circuit breaker as a safety net.
            p.Retry.MaxAttempts           = 1;
            p.Circuit.FailureThreshold    = 5;
            p.Circuit.OpenDurationSeconds = 30;
            p.Circuit.SuccessThreshold    = 2;
            p.Circuit.OnlyCountTransient  = true;
            p.Timeout.TimeoutMs           = 30_000;
        });
    }
}
