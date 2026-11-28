// ============================================================================
// filepath: src/DeploymentVault.Platform/KeyMaster/Generation/KeyGeneratorRegistry.cs
// layer:    Platform / Generation
// module:   DeploymentVault.Platform.KeyMaster.Generation
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  Dispatches GenerationSpec.KeyKind to the registered IKeyGenerator
//           and runs every generation through the keymaster-generate
//           resilience policy. Batch generation loops single calls — each
//           key is an independent CSPRNG draw.
// RELATIONSHIPS
//   Implements : IKeyGeneratorRegistry
//   Used by    : KeyMasterViewModel
//   See also   : IKeyGenerator, KeyMasterPolicyNames
// ============================================================================

using DeploymentVault.Core.KeyMaster.Abstractions;
using DeploymentVault.Core.KeyMaster.Domain;
using DeploymentVault.Platform.KeyMaster.Composition;
using Portfolio.Resilience.Abstractions;

namespace DeploymentVault.Platform.KeyMaster.Generation;

/// <summary>
/// Registry that maps <see cref="KeyKind"/> to generators and executes
/// generation through the resilience executor.
/// </summary>
public sealed class KeyGeneratorRegistry : IKeyGeneratorRegistry
{
    private readonly Dictionary<KeyKind, IKeyGenerator> _generators;
    private readonly IResilienceExecutor _executor;

    /// <summary>
    /// Creates the registry from the registered generators.
    /// </summary>
    /// <param name="generators">All IKeyGenerator implementations.</param>
    /// <param name="executor">The resilience executor. Not null.</param>
    /// <exception cref="ArgumentException">When two generators claim the same kind.</exception>
    public KeyGeneratorRegistry(IEnumerable<IKeyGenerator> generators, IResilienceExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(generators);
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));

        var map = new Dictionary<KeyKind, IKeyGenerator>();
        foreach (var gen in generators)
        {
            foreach (var kind in gen.Kinds)
            {
                if (!map.TryAdd(kind, gen))
                {
                    throw new ArgumentException(
                        $"Two generators claim {kind}.", nameof(generators));
                }
            }
        }
        _generators = map;
    }

    /// <inheritdoc/>
    public Task<GeneratedKey> GenerateAsync(GenerationSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var generator = Resolve(spec.Kind);
        return _executor.ExecuteAsync(
            KeyMasterPolicyNames.Generate,
            ct => generator.GenerateAsync(spec, ct),
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GeneratedKey>> GenerateBatchAsync(
        GenerationSpec spec, int count, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (count < 1 || count > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Batch size must be 1-100.");
        }

        var results = new List<GeneratedKey>(count);
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await GenerateAsync(spec, cancellationToken).ConfigureAwait(false));
        }
        return results;
    }

    private IKeyGenerator Resolve(KeyKind kind) =>
        _generators.TryGetValue(kind, out var gen)
            ? gen
            : throw new ArgumentException($"No generator registered for {kind}.", nameof(kind));
}
