// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Registry/RegistryServiceCollectionExtensions.cs
// PURPOSE:  Registers the runtime provider registry. Mirrors
//           AddFileHarvesterCloudCredentials and AddFileHarvesterCloudDispatch.
// RELATION: Called by App.axaml.cs BEFORE AddFileHarvesterCloudDispatch, so the
//           dispatcher provider factory resolves the registry. The dispatcher
//           factory resolves the registry lazily, so the order is not strict,
//           but the documented order is safest.
// ============================================================================

using Microsoft.Extensions.DependencyInjection;

namespace FileHarvester.Platform.Cloud.Registry;

/// <summary>
/// Registration for the runtime provider registry.
/// </summary>
public static class RegistryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the runtime provider registry. The registry is empty at
    /// startup; providers are registered later by the profile and
    /// connection flows.
    /// </summary>
    /// <param name="services">The service collection. Never null.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddFileHarvesterCloudRegistry(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ICloudProviderRegistry, CloudProviderRegistry>();

        return services;
    }
}
