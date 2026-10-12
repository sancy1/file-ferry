// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/OAuth/CloudOAuthServiceCollectionExtensions.cs
// PURPOSE:  Registers the OAuth services and the public connection service.
//           Registers the Google and Microsoft flows and the per-provider
//           flow registry. The App registers this extension and resolves
//           only the public ICloudConnectionService.
// RELATION: Called by App.axaml.cs. The token store resolves
//           ICredentialStore at first resolution, after all extensions.
// ============================================================================

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using FileHarvester.Core.Composition;
using FileHarvester.Platform.Cloud.Credentials;
using FileHarvester.Platform.Cloud.Services;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.Platform.Cloud.OAuth;

/// <summary>
/// Registers the OAuth cloud-connection services.
/// </summary>
public static class CloudOAuthServiceCollectionExtensions
{
    private static readonly TimeSpan CallbackTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Registers the two OAuth flows, the flow registry, the token store,
    /// the config store, and the public connection service.
    /// </summary>
    /// <param name="services">The service collection. Never null.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddFileHarvesterCloudOAuth(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<OAuthStateStore>();

        services.AddSingleton<Func<string, Task>>(_ => OpenBrowserAsync);

        services.AddSingleton<Func<int, string, TimeSpan, IOAuthLoopbackListener>>(
            _ => (port, path, timeout) => new OAuthLoopbackListener(port, path, timeout));

        services.AddSingleton<Func<int, string, TimeSpan, IGoogleDrivePickerCallbackListener>>(
            _ => (port, path, timeout) => new GoogleDrivePickerLoopbackListener(port, path, timeout));

        // The live HTTP seam for the hand-rolled Microsoft exchange.
        services.AddSingleton<IOAuthHttpClient, HttpOAuthHttpClient>();

        // The Google flow.
        services.AddSingleton(sp => new GoogleOAuthFlow(
            sp.GetRequiredService<OAuthStateStore>(),
            sp.GetRequiredService<Func<string, Task>>(),
            sp.GetRequiredService<Func<int, string, TimeSpan, IOAuthLoopbackListener>>(),
            ct => FetchGoogleClientSecretAsync(sp, ct),
            CallbackTimeout));

        services.AddSingleton(sp => new GoogleDrivePickerOAuthFlow(
            sp.GetRequiredService<OAuthStateStore>(),
            sp.GetRequiredService<Func<string, Task>>(),
            sp.GetRequiredService<Func<int, string, TimeSpan, IGoogleDrivePickerCallbackListener>>(),
            ct => FetchGoogleClientSecretAsync(sp, ct),
            CallbackTimeout));
        services.AddSingleton<IGoogleDrivePickerOAuthFlow>(sp =>
            sp.GetRequiredService<GoogleDrivePickerOAuthFlow>());

        // The Microsoft flow.
        services.AddSingleton(sp => new MicrosoftOAuthFlow(
            sp.GetRequiredService<OAuthStateStore>(),
            sp.GetRequiredService<Func<string, Task>>(),
            sp.GetRequiredService<Func<int, string, TimeSpan, IOAuthLoopbackListener>>(),
            sp.GetRequiredService<IOAuthHttpClient>(),
            CallbackTimeout));

        // The Dropbox flow.
        services.AddSingleton(sp => new DropboxOAuthFlow(
            sp.GetRequiredService<OAuthStateStore>(),
            sp.GetRequiredService<Func<string, Task>>(),
            sp.GetRequiredService<Func<int, string, TimeSpan, IOAuthLoopbackListener>>(),
            sp.GetRequiredService<IOAuthHttpClient>(),
            CallbackTimeout));

        // The per-provider flow registry.
        services.AddSingleton<IOAuthFlowRegistry>(sp => new OAuthFlowRegistry(
            sp.GetRequiredService<GoogleOAuthFlow>(),
            sp.GetRequiredService<MicrosoftOAuthFlow>(),
            sp.GetRequiredService<DropboxOAuthFlow>()));

        // The token store.
        services.AddSingleton(sp => new OAuthTokenStore(
            sp.GetRequiredService<ITypedCredentialStore>(),
            sp.GetRequiredService<IOAuthFlowRegistry>()));

        services.AddSingleton<IGoogleDrivePickerDownloadService>(sp =>
            new GoogleDrivePickerDownloadService(
                sp.GetRequiredService<IOAuthClientConfigStore>(),
                (config, instance, ct) => GetConnectedGoogleAccessTokenAsync(sp, config, instance, ct),
                sp.GetRequiredService<IGoogleDrivePickerOAuthFlow>()));

        // The registration store.
        services.AddSingleton<IOAuthClientConfigStore>(sp => new OAuthClientConfigStore(
            GetStorageDirectory(),
            sp.GetRequiredService<IResilienceExecutor>()));

        // The public application-facing service.
        services.AddSingleton<ICloudConnectionService, CloudConnectionService>();

        return services;
    }

    private static string GetStorageDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileHarvester");
    }

    private static Task OpenBrowserAsync(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        });

        return Task.CompletedTask;
    }
    /// <summary>
    /// Reads the stored Google client secret at token-exchange time.
    /// The value is returned to the caller only; it is not logged,
    /// cached, or retained.
    /// </summary>
    /// <param name="sp">The service provider. Not null.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The stored secret, or null when none is stored.</returns>
    private static async Task<string?> FetchGoogleClientSecretAsync(
        IServiceProvider sp,
        CancellationToken ct)
    {
        var store = sp.GetRequiredService<ITypedCredentialStore>();
        var key = CredentialStoreKey.Static(
            "gdrive", OAuthClientConfig.ClientSecretInstanceName);
        return await store.ReadAsync(key, ct).ConfigureAwait(false);
    }

    private static async Task<string> GetConnectedGoogleAccessTokenAsync(
        IServiceProvider sp,
        OAuthClientConfig config,
        string instanceName,
        CancellationToken ct)
    {
        var tokens = await sp.GetRequiredService<OAuthTokenStore>()
            .GetAccessTokenAsync(
                config,
                CredentialStoreKey.OAuth("gdrive", instanceName),
                ct)
            .ConfigureAwait(false);
        return tokens.AccessToken;
    }
}
