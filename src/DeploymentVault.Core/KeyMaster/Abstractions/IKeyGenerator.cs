// ============================================================================
// filepath: src/DeploymentVault.Core/KeyMaster/Abstractions/IKeyGenerator.cs
// layer:    Core / Abstractions
// module:   DeploymentVault.Core.KeyMaster.Abstractions
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  Contract for one framework generator. Each implementation declares
//           the KeyKinds it serves via Kinds; the registry dispatches by kind.
//           Generation is async so slow asymmetric work (RSA-4096) can yield.
// RELATIONSHIPS
//   Implemented by : DeploymentVault.Platform.KeyMaster.Generation.*
//   Used by        : IKeyGeneratorRegistry, KeyMasterViewModel
//   See also       : GenerationSpec, GeneratedKey
// ============================================================================

using DeploymentVault.Core.KeyMaster.Domain;

namespace DeploymentVault.Core.KeyMaster.Abstractions;

/// <summary>
/// Generates keys for one or more <see cref="KeyKind"/> values.
/// </summary>
public interface IKeyGenerator
{
    /// <summary>The kinds this generator serves.</summary>
    IReadOnlyCollection<KeyKind> Kinds { get; }

    /// <summary>
    /// Generates one key for <paramref name="spec"/>.
    /// </summary>
    /// <param name="spec">The generation request. Kind must be in <see cref="Kinds"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated key. Never null.</returns>
    /// <exception cref="ArgumentException">
    /// When the spec violates the kind's hard rules (e.g., HS256 under 32 bytes).
    /// </exception>
    /// <exception cref="OperationCanceledException">When cancelled.</exception>
    Task<GeneratedKey> GenerateAsync(GenerationSpec spec, CancellationToken cancellationToken = default);
}
