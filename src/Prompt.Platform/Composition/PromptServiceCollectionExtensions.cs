// ============================================================================
// FILE:     src/Prompt.Platform/Composition/PromptServiceCollectionExtensions.cs
// PURPOSE:  The single DI entry point for the Prompt App feature. Registers
//           the prompt-* resilience policies and (in later phases) the
//           prompt stores, exporter and importer.
// RELATION: Called by the application composition root with a database path.
//           Verified by PromptPolicyRegistrationTests.
// NOTES:   ORDER CONTRACT (verified by probe, PROMPT-F-012 evidence):
//           AddPromptFeature MUST be called BEFORE AddFileHarvester so the
//           harvester registration remains the last (and therefore the
//           app-wide resolved) IResilienceExecutor. Prompt policies ride
//           the shared policy store and Prompt events flow to the
//           application-owned sinks (Log Drawer) automatically.
//           No store registrations yet: the store interfaces arrive in
//           Phase 1. Registering stubs before the contracts exist would
//           be vacuous.
// ============================================================================

using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Extensions;

namespace Prompt.Platform.Composition;

/// <summary>
/// DI registration entry point for the Prompt App.
/// </summary>
public static class PromptServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Prompt App resilience policies. Call this before
    /// AddFileHarvester.
    /// </summary>
    /// <param name="services">The service collection. Never null.</param>
    /// <param name="databasePath">
    /// Absolute path of the Prompt SQLite database file. The parent
    /// directory is created if missing. Never null or whitespace.
    /// </param>
    public static IServiceCollection AddPromptFeature(
        this IServiceCollection services,
        string databasePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        services.AddPortfolioResilience(PromptResilienceConfig.RegisterPolicies);
        return services;
    }
}
