// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Registry/BuiltInProviderRegistration.cs
// PURPOSE:  The public registration facade. Registers the built-in
//           providers the running app can resolve at startup: the
//           S3-compatible AWS profile, Azure Blob Storage, Google
//           Cloud Storage, Google Drive, OneDrive, and Dropbox. Keeps
//           the internal ProviderConstruction detail out of the App.
// RELATION: Called by App.axaml.cs after BuildServiceProvider. Resolves
//           the registry, the credential store, the resilience executor,
//           and the OAuth dependencies, and registers each profile with
//           its factory.
// NOTES
//   All six providers are registered: aws-s3, azure-blob, gcs, gdrive,
//   onedrive, dropbox. The OAuth providers (gdrive, onedrive, dropbox)
//   resolve their OAuth client registration and access token lazily, on
//   first resolve of the profile, not at registration time. A missing
//   OAuth client registration throws on first use, not at startup; the
//   app still launches.
//   A missing OAuth dependency in the service provider is a hard error:
//   the method throws InvalidOperationException naming the missing
//   dependency. The OAuth registration is not silently skipped.
//   A missing profile id is a no-op, not a throw: the app ships a fixed
//   profile set, and a profile absent from BuiltInProfiles means the
//   build is misconfigured, not that the user did anything wrong. The
//   method does not swallow a failure from a present profile.
// ============================================================================

using System;
using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Credentials;
using FileHarvester.Platform.Cloud.OAuth;
using FileHarvester.Platform.Cloud.Providers;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.Platform.Cloud.Registry;

/// <summary>
/// Registers the built-in cloud providers the app can resolve.
/// </summary>
/// <remarks>
/// The App calls <see cref="RegisterBuiltInProviders"/> once, after the
/// service provider is built. This class resolves the collaborators and
/// delegates construction to the internal <c>ProviderConstruction</c>.
/// The App never sees the construction detail.
/// </remarks>
public static class BuiltInProviderRegistration
{
    /// <summary>
    /// Registers the six built-in providers: S3, Azure Blob, GCS,
    /// Google Drive, OneDrive, and Dropbox.
    /// </summary>
    /// <param name="serviceProvider">
    /// The built service provider. Not null. It must contain
    /// ICloudProviderRegistry, ICredentialStore, IResilienceExecutor,
    /// OAuthTokenStore, and IOAuthClientConfigStore.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// When <paramref name="serviceProvider"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A required service is not registered in the service provider.
    /// The message names the missing service.
    /// </exception>
    public static void RegisterBuiltInProviders(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var registry = (ICloudProviderRegistry)serviceProvider.GetService(typeof(ICloudProviderRegistry))!;
        var store = (ICredentialStore)serviceProvider.GetService(typeof(ICredentialStore))!;
        var resilience = (IResilienceExecutor)serviceProvider.GetService(typeof(IResilienceExecutor))!;

        if (registry is null)
        {
            throw new InvalidOperationException(
                "ICloudProviderRegistry is not registered in the service provider.");
        }

        if (store is null)
        {
            throw new InvalidOperationException(
                "ICredentialStore is not registered in the service provider.");
        }

        if (resilience is null)
        {
            throw new InvalidOperationException(
                "IResilienceExecutor is not registered in the service provider.");
        }

        var oauthTokenStore = (OAuthTokenStore?)serviceProvider.GetService(typeof(OAuthTokenStore));
        var oauthConfigStore = (IOAuthClientConfigStore?)serviceProvider.GetService(typeof(IOAuthClientConfigStore));

        if (oauthTokenStore is null || oauthConfigStore is null)
        {
            throw new InvalidOperationException(
                "OAuth dependencies are missing from the service provider: " +
                (oauthTokenStore is null ? "OAuthTokenStore " : "") +
                (oauthConfigStore is null ? "IOAuthClientConfigStore" : "") + ".");
        }

        var construction = new ProviderConstruction(store, resilience);

        RegisterIfPresent(
            registry,
            "aws-s3",
            construction.CreateS3("aws-s3"));

        RegisterIfPresent(
            registry,
            "azure-blob",
            construction.CreateAzureBlob("azure-blob"));

        RegisterIfPresent(
            registry,
            "gcs",
            construction.CreateGcs("gcs"));

        var oauthConstruction = new ProviderConstruction(
            store,
            resilience,
            oauthTokenStore,
            oauthConfigStore,
            new GraphDriveIdResolver());

        RegisterIfPresent(
            registry,
            "gdrive",
            oauthConstruction.CreateGoogleDrive("gdrive"));

        RegisterIfPresent(
            registry,
            "onedrive",
            oauthConstruction.CreateOneDrive("onedrive"));

        RegisterIfPresent(
            registry,
            "dropbox",
            oauthConstruction.CreateDropbox("dropbox"));
    }

    /// <summary>
    /// Registers a profile when it exists in the built-in set. A missing
    /// profile is a no-op: the build ships the profiles this method names,
    /// so a missing one indicates a build configuration problem rather
    /// than a user error. The method does not swallow a failure from a
    /// present profile: registration is synchronous and cannot fail here.
    /// </summary>
    private static void RegisterIfPresent(
        ICloudProviderRegistry registry,
        string profileId,
        Func<string?, System.Threading.CancellationToken, System.Threading.Tasks.Task<ICloudProvider>> factory)
    {
        var profile = BuiltInProfiles.FindById(profileId);
        if (profile is null)
        {
            return;
        }

        registry.Register(profile, factory);
    }
}
