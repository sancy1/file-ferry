// ============================================================================
// filepath: src/DeploymentVault.Core/KeyMaster/Abstractions/IKeyGeneratorRegistry.cs
// layer:    Core / Abstractions
// module:   DeploymentVault.Core.KeyMaster.Abstractions
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  Dispatch contract: resolve the generator for a KeyKind and run a
//           generation through the resilience executor. The registry is the
//           single entry point the ViewModel uses.
// RELATIONSHIPS
//   Implemented by : KeyGeneratorRegistry (DeploymentVault.Platform.KeyMaster)
//   Used by        : KeyMasterViewModel
//   See also       : IKeyGenerator, GenerationSpec, GeneratedKey
// ============================================================================

using DeploymentVault.Core.KeyMaster.Domain;

namespace DeploymentVault.Core.KeyMaster.Abstractions;

/// <summary>
/// Resolves generators by <see cref="KeyKind"/> and executes generation
/// through the keymaster-generate resilience policy.
/// </summary>
public interface IKeyGeneratorRegistry
{
    /// <summary>Generates one key for the spec's kind.</summary>
    /// <param name="spec">The generation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated key.</returns>
    Task<GeneratedKey> GenerateAsync(GenerationSpec spec, CancellationToken cancellationToken = default);

    /// <summary>Generates <paramref name="count"/> independent keys for the spec.</summary>
    /// <param name="spec">The generation request (its Count is ignored; use this parameter).</param>
    /// <param name="count">Batch size, 1-100.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated keys, in order.</returns>
    Task<IReadOnlyList<GeneratedKey>> GenerateBatchAsync(GenerationSpec spec, int count, CancellationToken cancellationToken = default);
}
