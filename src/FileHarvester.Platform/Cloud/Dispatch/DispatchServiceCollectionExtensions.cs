// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Dispatch/DispatchServiceCollectionExtensions.cs
// PURPOSE:  Registers the three dispatchers as the primary IFileResolver,
//           IContentReader, and IOutputSink, and registers the async
//           instance-aware provider factory the cloud services consume.
//           Called by the App after AddFileHarvesterCloudCredentials.
//           The dispatchers route by scheme: local to the ILocal*
//           implementations, cloud to the ICloud* implementations.
// RELATION: Called by App.axaml.cs. Lives in Platform, not Core, because
//           Core does not reference Platform. The cloud services and the
//           dispatchers are Platform types.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Services;
using FileHarvester.Platform.Cloud.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FileHarvester.Platform.Cloud.Dispatch;

/// <summary>
/// Registers the cloud dispatch layer with the DI container.
/// </summary>
/// <remarks>
/// <para>
/// Must be called AFTER AddFileHarvester (which registers the local
/// implementations as their ILocal* interfaces) and AFTER
/// AddFileHarvesterCloudCredentials (which registers ICredentialStore).
/// The dispatcher registrations override the primary-interface
/// registrations because DI resolves the last registration for a
/// single-service interface.
/// </para>
/// <para>
/// The provider factory is asynchronous and instance-aware. It resolves
/// a provider from the registry by scheme and optional instance. Until a
/// provider is registered, any cloud request produces the "no provider
/// registered for the scheme" note from the cloud resolver. Local
/// requests are unaffected.
/// </para>
/// </remarks>
public static class DispatchServiceCollectionExtensions
{
    /// <summary>
    /// Adds the cloud dispatch services.
    /// </summary>
    /// <param name="services">The service collection. Never null.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddFileHarvesterCloudDispatch(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The provider registry. Register a default non-destructively so
        // a caller that supplies its own registry keeps it.
        services.TryAddSingleton<ICloudProviderRegistry, CloudProviderRegistry>();

        // The async instance-aware provider factory. The cloud services
        // take this delegate; the dispatcher path supplies the scheme and
        // the parsed instance, and the registry resolves the provider.
        services.AddSingleton<Func<PathSchemeKind, string?, CancellationToken, Task<CloudProviderResolution>>>(sp =>
        {
            var registry = sp.GetRequiredService<ICloudProviderRegistry>();
            return (scheme, instance, cancellationToken) =>
                registry.ResolveAsync(scheme, instance, cancellationToken);
        });

        // The cloud services. Each takes the provider factory; the
        // sink also takes the writer registry and the overwrite
        // confirmer (both already registered by AddFileHarvester).
        services.AddSingleton<ICloudFileResolver, CloudFileResolver>();
        services.AddSingleton<ICloudContentReader, CloudContentReader>();
        services.AddSingleton<ICloudOutputSink, CloudOutputSink>();

        // The dispatchers. Registering the dispatcher for a
        // primary interface overrides the earlier local
        // registration because DI resolves the last registration.
        services.AddSingleton<IFileResolver, DispatchingFileResolver>();
        services.AddSingleton<IContentReader, DispatchingContentReader>();
        services.AddSingleton<IOutputSink, DispatchingOutputSink>();

        return services;
    }
}
