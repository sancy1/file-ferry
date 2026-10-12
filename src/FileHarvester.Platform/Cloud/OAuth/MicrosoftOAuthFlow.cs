// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/OAuth/MicrosoftOAuthFlow.cs
// PURPOSE:  IOAuthFlow implementation for Microsoft (OneDrive). A
//           hand-rolled PKCE authorization-code flow over an injectable
//           IOAuthHttpClient. Mirrors GoogleOAuthFlow structure: the same
//           state store, browser delegate, and listener factory. Microsoft
//           has no Google-style revocation endpoint; RevokeAsync is a
//           documented no-op and the durable token is deleted by
//           OAuthTokenStore.
// RELATION: Implements IOAuthFlow. Selected for onedrive by
//           OAuthFlowRegistry.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.OAuth;

/// <summary>The Microsoft OneDrive OAuth flow.</summary>
internal sealed class MicrosoftOAuthFlow : IOAuthFlow
{
    private readonly OAuthStateStore _stateStore;
    private readonly Func<string, Task> _openBrowser;
    private readonly Func<int, string, TimeSpan, IOAuthLoopbackListener> _listenerFactory;
    private readonly IOAuthHttpClient _http;
    private readonly TimeSpan _callbackTimeout;

    public MicrosoftOAuthFlow(
        OAuthStateStore stateStore,
        Func<string, Task> openBrowser,
        Func<int, string, TimeSpan, IOAuthLoopbackListener> listenerFactory,
        IOAuthHttpClient http,
        TimeSpan callbackTimeout)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _openBrowser = openBrowser ?? throw new ArgumentNullException(nameof(openBrowser));
        _listenerFactory = listenerFactory ?? throw new ArgumentNullException(nameof(listenerFactory));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (callbackTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(callbackTimeout), "Callback timeout must be positive.");
        }
        _callbackTimeout = callbackTimeout;
    }

    /// <inheritdoc />
    public async Task<OAuthTokens> AuthorizeAsync(
        OAuthClientConfig config,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);

        var state = OAuthStateStore.NewState();
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        _stateStore.Store(state, verifier);

        var url = config.AuthorizationEndpoint
            + "?client_id=" + Uri.EscapeDataString(config.ClientId)
            + "&response_type=code"
            + "&redirect_uri=" + Uri.EscapeDataString(config.RedirectUri)
            + "&scope=" + Uri.EscapeDataString(string.Join(" ", config.Scopes))
            + "&state=" + Uri.EscapeDataString(state)
            + "&code_challenge=" + Uri.EscapeDataString(challenge)
            + "&code_challenge_method=S256";

        var port = new Uri(config.RedirectUri, UriKind.Absolute).Port;
        var path = new Uri(config.RedirectUri, UriKind.Absolute).AbsolutePath;
        await using var listener = _listenerFactory(port, path, _callbackTimeout);
        var waitTask = listener.WaitForCallbackAsync(cancellationToken);
        await _openBrowser(url).ConfigureAwait(false);
        var callback = await waitTask.ConfigureAwait(false);

        if (callback.Error is not null)
        {
            if (string.Equals(callback.Error, "access_denied", StringComparison.Ordinal))
            {
                throw new OAuthConsentDeniedException();
            }
            throw new OAuthCallbackException(callback.Error);
        }

        if (string.IsNullOrEmpty(callback.Code) || string.IsNullOrEmpty(callback.State))
        {
            throw new OAuthCallbackException(errorCode: null);
        }

        if (!string.Equals(callback.State, state, StringComparison.Ordinal))
        {
            throw new OAuthStateException("returned state did not match the issued state");
        }

        var storedVerifier = _stateStore.Consume(callback.State);

        var form = new Dictionary<string, string>
        {
            ["client_id"] = config.ClientId,
            ["grant_type"] = "authorization_code",
            ["code"] = callback.Code,
            ["redirect_uri"] = config.RedirectUri,
            ["code_verifier"] = storedVerifier,
        };

        var body = await _http.PostFormAsync(config.TokenEndpoint, form, cancellationToken)
            .ConfigureAwait(false);
        return ParseTokens(body);
    }

    /// <inheritdoc />
    public async Task<OAuthTokens> RefreshAsync(
        OAuthClientConfig config,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        var form = new Dictionary<string, string>
        {
            ["client_id"] = config.ClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        };

        var body = await _http.PostFormAsync(config.TokenEndpoint, form, cancellationToken)
            .ConfigureAwait(false);

        if (body.Contains("invalid_grant", StringComparison.Ordinal))
        {
            throw new OAuthTokenRevokedException();
        }

        return ParseTokens(body);
    }

    /// <inheritdoc />
    public Task RevokeAsync(
        OAuthClientConfig config,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        // Microsoft has no Google-style revocation endpoint. This is a
        // documented no-op: it makes no network call and does not throw.
        // OAuthTokenStore deletes the durable token after this returns.
        _ = config;
        _ = refreshToken;
        _ = cancellationToken;
        return Task.CompletedTask;
    }

    private static OAuthTokens ParseTokens(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var access = root.TryGetProperty("access_token", out var a) ? a.GetString() ?? string.Empty : string.Empty;
        var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        var scope = root.TryGetProperty("scope", out var s) ? s.GetString() : null;
        var seconds = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var v) ? v : 3600;
        return new OAuthTokens(access, DateTime.UtcNow.AddSeconds(seconds), refresh, scope);
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd((char)61).Replace((char)43, (char)45).Replace((char)47, (char)95);
}
