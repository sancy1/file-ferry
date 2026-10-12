// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Credentials/CredentialStoreServiceCollectionExtensions.cs
// PURPOSE:  The DI extension the App calls to register the credential
//           store. The OS-specific concrete store is registered once as a
//           singleton; both ICredentialStore and ITypedCredentialStore
//           resolve that same instance, so a static write and a typed
//           write go to the same underlying store.
// RELATION: Called from App.axaml.cs immediately after AddFileHarvester.
//           Lives in FileHarvester.Platform, not Core, because Core does
//           not reference Platform.
// ============================================================================

using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;

namespace FileHarvester.Platform.Cloud.Credentials;

/// <summary>
/// Registration for the platform credential store.
/// </summary>
public static class CredentialStoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the correct credential store for the host platform, once,
    /// and exposes it through both ICredentialStore (the static-credential
    /// surface) and ITypedCredentialStore (the kind-aware surface).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddFileHarvesterCloudCredentials(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (OperatingSystem.IsWindows())
        {
            return AddWindowsStore(services);
        }

        if (OperatingSystem.IsMacOS())
        {
            return AddMacOsStore(services);
        }

        if (OperatingSystem.IsLinux())
        {
            return AddLinuxStore(services);
        }

        return AddNotSupportedStore(services);
    }

    [SupportedOSPlatform("windows")]
    private static IServiceCollection AddWindowsStore(IServiceCollection services)
    {
        services.AddSingleton<WindowsCredentialStore>();
        services.AddSingleton<ICredentialStore>(
            sp => sp.GetRequiredService<WindowsCredentialStore>());
        services.AddSingleton<ITypedCredentialStore>(
            sp => sp.GetRequiredService<WindowsCredentialStore>());
        return services;
    }

    [SupportedOSPlatform("macos")]
    private static IServiceCollection AddMacOsStore(IServiceCollection services)
    {
        services.AddSingleton<MacOsKeychainStore>();
        services.AddSingleton<ICredentialStore>(
            sp => sp.GetRequiredService<MacOsKeychainStore>());
        services.AddSingleton<ITypedCredentialStore>(
            sp => sp.GetRequiredService<MacOsKeychainStore>());
        return services;
    }

    [SupportedOSPlatform("linux")]
    private static IServiceCollection AddLinuxStore(IServiceCollection services)
    {
        services.AddSingleton<LinuxSecretServiceStore>();
        services.AddSingleton<ICredentialStore>(
            sp => sp.GetRequiredService<LinuxSecretServiceStore>());
        services.AddSingleton<ITypedCredentialStore>(
            sp => sp.GetRequiredService<LinuxSecretServiceStore>());
        return services;
    }

    private static IServiceCollection AddNotSupportedStore(IServiceCollection services)
    {
        services.AddSingleton<NotSupportedCredentialStore>();
        services.AddSingleton<ICredentialStore>(
            sp => sp.GetRequiredService<NotSupportedCredentialStore>());
        services.AddSingleton<ITypedCredentialStore>(
            sp => sp.GetRequiredService<NotSupportedCredentialStore>());
        return services;
    }
}
