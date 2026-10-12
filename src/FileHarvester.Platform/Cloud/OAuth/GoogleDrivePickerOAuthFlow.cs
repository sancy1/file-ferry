using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using FileHarvester.Platform.Cloud.Providers;

namespace FileHarvester.Platform.Cloud.OAuth;

internal sealed record GoogleDrivePickerGrant(
    string AccessToken,
    DateTime ExpiresAtUtc,
    IReadOnlyList<string> FileIds);

internal sealed record GoogleDrivePickerAccessToken(
    string AccessToken,
    DateTime ExpiresAtUtc,
    string? Scope);

internal sealed record GooglePickerAuthorizationRequest(string Url, string CodeVerifier);

internal interface IGoogleDrivePickerOAuthFlow
{
    Task<GoogleDrivePickerGrant> AuthorizeAsync(OAuthClientConfig config, CancellationToken ct);
}

internal sealed class GoogleDrivePickerOAuthFlow : IGoogleDrivePickerOAuthFlow
{
    private const string UserId = "file-ferry-picker";
    private const string RequiredScope = OAuthScopes.DriveFile;
    private readonly OAuthStateStore _stateStore;
    private readonly Func<string, Task> _openBrowser;
    private readonly Func<int, string, TimeSpan, IGoogleDrivePickerCallbackListener> _listenerFactory;
    private readonly Func<CancellationToken, Task<string?>> _fetchClientSecret;
    private readonly Func<OAuthClientConfig, string, GooglePickerAuthorizationRequest> _createRequest;
    private readonly Func<OAuthClientConfig, string, string, string, CancellationToken, Task<GoogleDrivePickerAccessToken>> _exchange;
    private readonly TimeSpan _timeout;

    internal GoogleDrivePickerOAuthFlow(
        OAuthStateStore stateStore,
        Func<string, Task> openBrowser,
        Func<int, string, TimeSpan, IGoogleDrivePickerCallbackListener> listenerFactory,
        Func<CancellationToken, Task<string?>> fetchClientSecret,
        TimeSpan timeout,
        Func<OAuthClientConfig, string, GooglePickerAuthorizationRequest>? createRequest = null,
        Func<OAuthClientConfig, string, string, string, CancellationToken, Task<GoogleDrivePickerAccessToken>>? exchange = null)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _openBrowser = openBrowser ?? throw new ArgumentNullException(nameof(openBrowser));
        _listenerFactory = listenerFactory ?? throw new ArgumentNullException(nameof(listenerFactory));
        _fetchClientSecret = fetchClientSecret ?? throw new ArgumentNullException(nameof(fetchClientSecret));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        _timeout = timeout;
        _createRequest = createRequest ?? CreateAuthorizationRequest;
        _exchange = exchange ?? ExchangeCodeAsync;
    }

    public async Task<GoogleDrivePickerGrant> AuthorizeAsync(
        OAuthClientConfig config,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Scopes.Count != 1
            || !string.Equals(config.Scopes[0], RequiredScope, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Google Drive Picker requires the drive.file scope only.");
        }

        var secret = await _fetchClientSecret(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("The Google Client Secret has not been saved. Save it before connecting.");
        }

        var authorization = _createRequest(config, secret);
        var state = OAuthStateStore.NewState();
        _stateStore.Store(state, authorization.CodeVerifier);
        var authUri = new Uri(authorization.Url, UriKind.Absolute);
        var redirectUri = new Uri(config.RedirectUri, UriKind.Absolute);
        if (!string.Equals(authUri.Host, "accounts.google.com", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(redirectUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(redirectUri.Host, "127.0.0.1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Google Drive Picker requires the configured Google endpoint and loopback redirect.");
        }

        var listener = _listenerFactory(redirectUri.Port, redirectUri.AbsolutePath, _timeout);
        await using (listener.ConfigureAwait(false))
        {
            var wait = listener.WaitAsync(ct);
            var url = AddPickerParameters(authorization.Url, state);
            await _openBrowser(url).ConfigureAwait(false);
            var callback = await wait.ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(callback.State)
                || !string.Equals(callback.State, state, StringComparison.Ordinal))
            {
                throw new OAuthStateException("returned state did not match the issued state");
            }
            var verifier = _stateStore.Consume(callback.State);

            if (!string.IsNullOrEmpty(callback.Error))
            {
                if (string.Equals(callback.Error, "access_denied", StringComparison.Ordinal))
                {
                    throw new OAuthConsentDeniedException();
                }
                throw new OAuthCallbackException("picker_authorization_failed");
            }
            if (string.IsNullOrWhiteSpace(callback.Code)
                || string.IsNullOrWhiteSpace(callback.State)
                || callback.FileIds.Count == 0)
            {
                throw new OAuthCallbackException("invalid_picker_response");
            }
            var tokens = await _exchange(
                config, secret, callback.Code, verifier, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(tokens.AccessToken)
                || !HasOnlyRequiredScope(callback.Scope)
                || (tokens.Scope is not null && !HasOnlyRequiredScope(tokens.Scope)))
            {
                throw new OAuthCallbackException("picker_scope_missing");
            }

            return new GoogleDrivePickerGrant(
                tokens.AccessToken,
                tokens.ExpiresAtUtc,
                callback.FileIds);
        }
    }

    internal static string AddPickerParameters(string authorizationUrl, string state)
    {
        var uri = new UriBuilder(authorizationUrl);
        var query = HttpUtility.ParseQueryString(uri.Query);
        query["state"] = state;
        query["prompt"] = "consent";
        query["trigger_onepick"] = "true";
        query["allow_multiple"] = "true";
        uri.Query = query.ToString() ?? string.Empty;
        return uri.Uri.AbsoluteUri;
    }

    private static bool HasOnlyRequiredScope(string? scope)
    {
        var scopes = (scope ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return scopes.Length == 1 && string.Equals(scopes[0], RequiredScope, StringComparison.Ordinal);
    }

    private static GooglePickerAuthorizationRequest CreateAuthorizationRequest(
        OAuthClientConfig config,
        string secret)
    {
        var flow = CreatePkceFlow(config, secret);
        var authRequest = flow.CreateAuthorizationCodeRequest(config.RedirectUri, out var verifier);
        return new GooglePickerAuthorizationRequest(authRequest.Build().AbsoluteUri, verifier);
    }

    private static async Task<GoogleDrivePickerAccessToken> ExchangeCodeAsync(
        OAuthClientConfig config,
        string secret,
        string code,
        string verifier,
        CancellationToken ct)
    {
        var flow = CreatePkceFlow(config, secret);
        var token = await flow.ExchangeCodeForTokenAsync(
            UserId, code, verifier, config.RedirectUri, ct).ConfigureAwait(false);
        var expires = token.ExpiresInSeconds is { } seconds
            ? token.IssuedUtc.AddSeconds(seconds)
            : token.IssuedUtc;
        return new GoogleDrivePickerAccessToken(
            token.AccessToken ?? string.Empty,
            expires,
            token.Scope);
    }

    private static PkceGoogleAuthorizationCodeFlow CreatePkceFlow(
        OAuthClientConfig config,
        string secret) =>
        new(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = config.ClientId,
                ClientSecret = secret,
            },
            Scopes = new[] { RequiredScope },
        });
}
