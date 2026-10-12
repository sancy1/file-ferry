// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Discovery/DiscoveryServiceCollectionExtensions.cs
// PURPOSE:  Registers the provider discovery services: the name catalog and
//           the local name resolver. Mirrors AddFileHarvesterCloudCredentials
//           and AddFileHarvesterCloudDispatch.
// RELATION: Called by App.axaml.cs. The remote resolver is registered in a
//           later sequence step and is not part of this extension yet.
// ============================================================================

using Microsoft.Extensions.DependencyInjection;

namespace FileHarvester.Platform.Cloud.Discovery;

/// <summary>
/// Registration for provider discovery and name normalization.
/// </summary>
public static class DiscoveryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the name catalog and the local catalog resolver.
    /// </summary>
    /// <param name="services">The service collection. Never null.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddFileHarvesterCloudDiscovery(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The catalog is loaded once from the embedded resource and shared.
        services.AddSingleton<CloudNameCatalog>();

        // The local resolver is the default ICloudNameResolver.
        services.AddSingleton<ICloudNameResolver, LocalCatalogResolver>();

        return services;
    }
}
