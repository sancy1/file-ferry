// ============================================================================
// filepath: src/DeploymentVault.Platform/Composition/DeploymentVaultServiceCollectionExtensions.cs
// layer:    Composition
// module:   DeploymentVault.Platform.Composition
// since:    Phase A (Deployment Vault registry)
// purpose:  DI registration for the Deployment Vault. Registers the
//           schema bootstrap and the SQLite store as singletons.
//
// RELATIONSHIPS
//   Implements : n/a
//   Depends on : DeploymentVaultSchemaBootstrap, SqliteDeploymentVaultStore,
//                Microsoft.Extensions.DependencyInjection
//   Used by    : FileHarvester.App/App.axaml.cs
//   See also   : IDeploymentVaultStore, SqliteDeploymentVaultStore
//
// NOTES
//   - No Portfolio.Resilience dependency. Per F-014 the vault does not use
//     the resilience library; the store throws directly. This extension
//     therefore does not take an IResilienceExecutor and does not call
//     AddPortfolioResilience.
//   - The database path is <ApplicationData>/DeploymentVault/vault.db.
//     The parent directory is created by DeploymentVaultSchemaBootstrap's
//     constructor; the caller does not need to pre-create it.
//   - Both the bootstrap and the store are singletons. The bootstrap owns
//     a SemaphoreSlim (disposed by the container on host shutdown); the
//     store is stateless.
// ============================================================================

using DeploymentVault.Core.Abstractions;
using DeploymentVault.Core.EnvSafe.Abstractions;
using DeploymentVault.Core.KeyForge.Abstractions;
using DeploymentVault.Platform.EnvSafe.Conversion;
using DeploymentVault.Platform.EnvSafe.Documents;
using DeploymentVault.Platform.EnvSafe.Safety;
using DeploymentVault.Platform.EnvSafe.Validation;
using DeploymentVault.Platform.Export;
using DeploymentVault.Platform.KeyForge.Documents;
using DeploymentVault.Platform.KeyForge.Extraction;
using DeploymentVault.Platform.KeyForge.Forgery;
using DeploymentVault.Platform.Storage;
using FileHarvester.Core.Writers;
using Microsoft.Extensions.DependencyInjection;

namespace DeploymentVault.Platform.Composition;

/// <summary>
/// DI registration for the Deployment Vault.
/// </summary>
public static class DeploymentVaultServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Deployment Vault schema bootstrap and store.
    /// </summary>
    /// <param name="services">The service collection. Never null.</param>
    /// <param name="databasePath">
    /// Absolute path of the SQLite database file. The parent directory is
    /// created if missing. Never null or whitespace.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> instance, for chaining.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// When <paramref name="databasePath"/> is null, empty, or whitespace.
    /// </exception>
    public static IServiceCollection AddDeploymentVaultStores(
        this IServiceCollection services,
        string databasePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        services.AddSingleton(sp => new DeploymentVaultSchemaBootstrap(databasePath));
        services.AddSingleton(sp => new SqliteDeploymentVaultStore(
            sp.GetRequiredService<DeploymentVaultSchemaBootstrap>()));
        services.AddSingleton<IDeploymentVaultStore>(sp =>
            sp.GetRequiredService<SqliteDeploymentVaultStore>());
        services.AddSingleton<IVaultExporter>(sp =>
            new VaultExporter(sp.GetRequiredService<IFormatWriterRegistry>()));

        // EnvSafe (sub-app of the vault): stateless services, safe to share.
        services.AddSingleton<IEnvFileValidator, EnvFileValidator>();
        services.AddSingleton<IEnvFileConverter, EnvFileConverter>();
        services.AddSingleton<ISafetyScanner, SafetyScanner>();
        services.AddSingleton<IEnvFileReader, EnvFileReader>();

        // KeyForge (sub-app of the vault): stateless services, safe to share.
        services.AddSingleton<IKeyExtractor, RegexKeyExtractor>();
        services.AddSingleton<IKeyForger, ShapePreservingKeyForger>();
        services.AddSingleton<IDocumentTextReader, KeyForgeDocumentReader>();

        return services;
    }
}