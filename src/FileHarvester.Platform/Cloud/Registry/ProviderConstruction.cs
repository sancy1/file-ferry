// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Registry/ProviderConstruction.cs
// PURPOSE:  The profile-to-provider adapter. Supplies the async
//           instance-aware factories the registry stores: each resolves
//           the credential instance, reads the stored credential, builds
//           the SDK client, wraps it in the provider adapter, and
//           constructs the provider with the injected resilience executor.
// RELATION: Used by the startup registration in App.axaml.cs. Depends on
//           ICredentialStore, IResilienceExecutor, and the four provider
//           factories and adapters in Cloud/Providers.
// NOTES
//   The factory applies the credential instance rule:
//     - instance != null: use exactly that instance; fail if absent.
//     - instance == null: exactly one credential must exist; fail on
//       zero or on more than one. Never silently choose an account.
//   Credential-selection failures throw CloudCredentialResolutionException
//   with a CloudResolutionFailure reason. Malformed or unusable stored
//   credential data is a construction fault (ProviderConstructionFailed).
//   No message contains a credential value.
//   Google Drive is not registered: it needs an OAuth token that does
//   not exist until the live-OAuth milestone. OneDrive and Dropbox have
//   no provider adapters (F-103). Only S3, Azure Blob, and GCS are built.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Dropbox.Api;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using FileHarvester.Core.Domain;
using FileHarvester.Platform.Cloud.Credentials;
using FileHarvester.Platform.Cloud.Providers;
using FileHarvester.Platform.Cloud.OAuth;
using Microsoft.Graph;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.Platform.Cloud.Registry;

/// <summary>
/// Supplies the async instance-aware provider factories the registry
/// stores.
/// </summary>
internal sealed class ProviderConstruction
{
    private readonly ICredentialStore _store;
    private readonly IResilienceExecutor _resilience;

    public ProviderConstruction(
        ICredentialStore store,
        IResilienceExecutor resilience)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _resilience = resilience ?? throw new ArgumentNullException(nameof(resilience));
    }

    private readonly OAuthTokenStore? _oauthTokenStore;
    private readonly IOAuthClientConfigStore? _oauthClientConfigStore;
    private readonly IOneDriveDriveIdResolver? _oneDriveDriveIdResolver;

    /// <summary>
    /// Creates the helper with the OAuth dependencies OneDrive needs.
    /// </summary>
    /// <param name="store">The credential store. Not null.</param>
    /// <param name="resilience">The resilience executor. Not null.</param>
    /// <param name="oauthTokenStore">The OAuth token store. Not null.</param>
    /// <param name="oauthClientConfigStore">The OAuth config store. Not null.</param>
    /// <param name="oneDriveDriveIdResolver">The OneDrive drive-id resolver. Not null.</param>
    public ProviderConstruction(
        ICredentialStore store,
        IResilienceExecutor resilience,
        OAuthTokenStore oauthTokenStore,
        IOAuthClientConfigStore oauthClientConfigStore,
        IOneDriveDriveIdResolver oneDriveDriveIdResolver)
        : this(store, resilience)
    {
        _oauthTokenStore = oauthTokenStore ?? throw new ArgumentNullException(nameof(oauthTokenStore));
        _oauthClientConfigStore = oauthClientConfigStore ?? throw new ArgumentNullException(nameof(oauthClientConfigStore));
        _oneDriveDriveIdResolver = oneDriveDriveIdResolver ?? throw new ArgumentNullException(nameof(oneDriveDriveIdResolver));
    }

    public Func<string?, CancellationToken, Task<ICloudProvider>> CreateS3(string profileId)
    {
        return async (instance, ct) =>
        {
            var values = await ReadValuesAsync(profileId, instance, ct).ConfigureAwait(false);
            var accessKeyId = Required(values, "accessKeyId");
            var secretAccessKey = Required(values, "secretAccessKey");
            var sessionToken = Optional(values, "sessionToken");
            var region = Optional(values, "region");
            var endpointUrl = Optional(values, "endpointUrl");
            var forcePathStyle = !string.IsNullOrWhiteSpace(endpointUrl);
            var settings = new S3ConnectionSettings(
                AccessKeyId: accessKeyId,
                SecretAccessKey: secretAccessKey,
                SessionToken: sessionToken,
                Region: region,
                CustomEndpointUrl: endpointUrl,
                ForcePathStyle: forcePathStyle);
            var client = S3ProviderFactory.CreateClient(settings);
            return new S3CompatibleProvider(client, _resilience);
        };
    }

    public Func<string?, CancellationToken, Task<ICloudProvider>> CreateAzureBlob(string profileId)
    {
        return async (instance, ct) =>
        {
            var values = await ReadValuesAsync(profileId, instance, ct).ConfigureAwait(false);
            var connectionString = Required(values, "connectionString");
            var settings = new AzureBlobConnectionSettings(
                AccountName: ExtractAccountName(connectionString));
            var client = AzureBlobProviderFactory.CreateClient(
                settings,
                AzureBlobAuthKind.ConnectionString,
                connectionString);
            return new AzureBlobProvider(
                new AzureBlobClientAdapter(client),
                _resilience);
        };
    }

    public Func<string?, CancellationToken, Task<ICloudProvider>> CreateGcs(string profileId)
    {
        return async (instance, ct) =>
        {
            var values = await ReadValuesAsync(profileId, instance, ct).ConfigureAwait(false);
            var serviceAccountJson = Required(values, "serviceAccountJson");
            var projectId = ExtractProjectId(serviceAccountJson);
            var settings = new GcsConnectionSettings(
                ProjectId: projectId);
            var client = GcsProviderFactory.CreateClient(settings, serviceAccountJson);
            return new GcsObjectProvider(
                new GcsClientAdapter(client),
                _resilience);
        };
    }

    private async Task<Dictionary<string, string>> ReadValuesAsync(
        string profileId,
        string? instance,
        CancellationToken ct)
    {
        var instanceName = await ResolveInstanceAsync(profileId, instance, ct).ConfigureAwait(false);

        var key = new CredentialKey(profileId, instanceName);
        var payload = await _store.ReadAsync(key, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new CloudCredentialResolutionException(
                CloudResolutionFailure.NoCredential,
                "No stored credential for provider [" + profileId +
                "] instance [" + instanceName + "].");
        }

        Dictionary<string, string>? values;
        try
        {
            values = JsonSerializer.Deserialize<Dictionary<string, string>>(payload);
        }
        catch (JsonException)
        {
            throw new CloudCredentialResolutionException(
                CloudResolutionFailure.ProviderConstructionFailed,
                "Stored credential for provider [" + profileId +
                "] is not a valid value map.");
        }

        if (values is null)
        {
            throw new CloudCredentialResolutionException(
                CloudResolutionFailure.ProviderConstructionFailed,
                "Stored credential for provider [" + profileId +
                "] is not a valid value map.");
        }

        return values;
    }

    private async Task<string> ResolveInstanceAsync(
        string profileId,
        string? instance,
        CancellationToken ct)
    {
        var names = await _store.ListInstanceNamesAsync(profileId, ct).ConfigureAwait(false);

        if (instance is not null)
        {
            foreach (var name in names)
            {
                if (string.Equals(name, instance, StringComparison.Ordinal))
                {
                    return name;
                }
            }

            throw new CloudCredentialResolutionException(
                CloudResolutionFailure.UnknownInstance,
                "No instance named [" + instance + "] for provider [" + profileId + "].");
        }

        if (names.Count == 0)
        {
            throw new CloudCredentialResolutionException(
                CloudResolutionFailure.NoCredential,
                "No stored credential for provider [" + profileId + "].");
        }

        if (names.Count > 1)
        {
            throw new CloudCredentialResolutionException(
                CloudResolutionFailure.MultipleCredentials,
                "Multiple stored credentials for provider [" + profileId +
                "]. Name one with an instance segment in the path.");
        }

        return names[0];
    }

    private static string Required(Dictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new CloudCredentialResolutionException(
                CloudResolutionFailure.ProviderConstructionFailed,
                "Stored credential is missing the required field [" + key + "].");
        }

        return value;
    }

    private static string? Optional(Dictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value;
    }

    private static string ExtractAccountName(string connectionString)
    {
        const string token = "AccountName=";
        var start = connectionString.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return string.Empty;

        start += token.Length;
        var end = connectionString.IndexOf((char)59, start);
        if (end < 0) end = connectionString.Length;

        return connectionString.Substring(start, end - start).Trim();
    }

    private static string ExtractProjectId(string serviceAccountJson)
    {
        using var doc = JsonDocument.Parse(serviceAccountJson);
        if (doc.RootElement.TryGetProperty("project_id", out var projectId)
            && projectId.ValueKind == JsonValueKind.String)
        {
            return projectId.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    /// <summary>
    /// The async instance-aware factory for Google Drive. Requires the
    /// OAuth-aware constructor; throws when the legacy constructor was
    /// used. The OAuth client config and access token flow mirror
    /// CreateOneDrive; the Drive client is built from the access token
    /// with a bearer authorization-header access method.
    /// </summary>
    /// <param name="profileId">The profile id (gdrive).</param>
    /// <returns>The registry factory delegate.</returns>
    /// <exception cref="InvalidOperationException">
    /// The OAuth dependencies are absent, or the OAuth client
    /// registration is absent.
    /// </exception>
    public Func<string?, CancellationToken, Task<ICloudProvider>> CreateGoogleDrive(string profileId)
    {
        if (_oauthTokenStore is null || _oauthClientConfigStore is null)
        {
            throw new InvalidOperationException(
                "Google Drive provider construction requires OAuth dependencies.");
        }

        return async (instance, ct) =>
        {
            var instanceName = await ResolveInstanceAsync(profileId, instance, ct)
                .ConfigureAwait(false);

            var config = await _oauthClientConfigStore
                .ReadAsync(profileId, ct)
                .ConfigureAwait(false);
            if (config is null)
            {
                throw new InvalidOperationException(
                    "No OAuth client registration for provider [" + profileId + "].");
            }

            var key = CredentialStoreKey.OAuth(profileId, instanceName);
            var tokens = await _oauthTokenStore
                .GetAccessTokenAsync(config, key, ct)
                .ConfigureAwait(false);

            var credential = GoogleCredential.FromAccessToken(
                tokens.AccessToken,
                new BearerToken.AuthorizationHeaderAccessMethod());
            var service = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "File-Ferry",
            });

            return new GoogleDriveProvider(
                new GDriveClientAdapter(new DriveServiceTransport(service)),
                _resilience);
        };
    }

    /// <summary>
    /// The async instance-aware factory for Microsoft OneDrive. Requires
    /// the OAuth-aware constructor; throws when the legacy constructor was
    /// used. Resolves the signed-in user default drive id through the
    /// injected IOneDriveDriveIdResolver before constructing the provider.
    /// </summary>
    /// <param name="profileId">The profile id (onedrive).</param>
    /// <returns>The registry factory delegate.</returns>
    /// <exception cref="InvalidOperationException">
    /// The OAuth dependencies are absent, the OAuth client registration is
    /// absent, or the stored token carries no expiry.
    /// </exception>
    public Func<string?, CancellationToken, Task<ICloudProvider>> CreateOneDrive(string profileId)
    {
        if (_oauthTokenStore is null
            || _oauthClientConfigStore is null
            || _oneDriveDriveIdResolver is null)
        {
            throw new InvalidOperationException(
                "OneDrive provider construction requires OAuth dependencies.");
        }

        return async (instance, ct) =>
        {
            var instanceName = await ResolveInstanceAsync(profileId, instance, ct)
                .ConfigureAwait(false);

            var config = await _oauthClientConfigStore
                .ReadAsync(profileId, ct)
                .ConfigureAwait(false);
            if (config is null)
            {
                throw new InvalidOperationException(
                    "No OAuth client registration for provider [" + profileId + "].");
            }

            var key = CredentialStoreKey.OAuth(profileId, instanceName);
            var tokens = await _oauthTokenStore
                .GetAccessTokenAsync(config, key, ct)
                .ConfigureAwait(false);

            if (tokens.ExpiresAtUtc == default)
            {
                throw new InvalidOperationException(
                    "Stored OAuth token for provider [" + profileId +
                    "] carries no expiry; refusing to construct with a fabricated lifetime.");
            }

            var credential = DelegatedTokenCredential.Create(
                (_, _) => new AccessToken(
                    tokens.AccessToken,
                    new DateTimeOffset(tokens.ExpiresAtUtc, TimeSpan.Zero)));

            var graph = new GraphServiceClient(credential);
            var driveIdProvider = new GraphDefaultDriveIdProvider(graph);
            var driveId = await _oneDriveDriveIdResolver
                .ResolveAsync(driveIdProvider, ct)
                .ConfigureAwait(false);

            return new OneDriveProvider(
                new MicrosoftGraphOneDriveClient(graph, driveId),
                _resilience);
        };
    }

    /// <summary>
    /// The async instance-aware factory for Dropbox. Requires the
    /// OAuth-aware constructor; throws when the legacy constructor was
    /// used.
    /// </summary>
    /// <param name="profileId">The profile id (dropbox).</param>
    /// <returns>The registry factory delegate.</returns>
    /// <exception cref="InvalidOperationException">
    /// The OAuth dependencies are absent, or the OAuth client
    /// registration is absent.
    /// </exception>
    public Func<string?, CancellationToken, Task<ICloudProvider>> CreateDropbox(string profileId)
    {
        if (_oauthTokenStore is null || _oauthClientConfigStore is null)
        {
            throw new InvalidOperationException(
                "Dropbox provider construction requires OAuth dependencies.");
        }

        return async (instance, ct) =>
        {
            var instanceName = await ResolveInstanceAsync(profileId, instance, ct)
                .ConfigureAwait(false);

            var config = await _oauthClientConfigStore
                .ReadAsync(profileId, ct)
                .ConfigureAwait(false);
            if (config is null)
            {
                throw new InvalidOperationException(
                    "No OAuth client registration for provider [" + profileId + "].");
            }

            var key = CredentialStoreKey.OAuth(profileId, instanceName);
            var tokens = await _oauthTokenStore
                .GetAccessTokenAsync(config, key, ct)
                .ConfigureAwait(false);

            var client = new DropboxClient(tokens.AccessToken);
            return new DropboxProvider(
                new DropboxClientAdapter(client),
                _resilience);
        };
    }
}
