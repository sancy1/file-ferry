// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/OAuth/DropboxOAuthFlow.cs
// PURPOSE:  IOAuthFlow implementation for Dropbox. A hand-rolled PKCE
//           authorization-code flow over an injectable IOAuthHttpClient.
//           Mirrors MicrosoftOAuthFlow structure: the same state store,
//           browser delegate, listener factory, and HTTP seam. Dropbox
//           provides a real token-revocation endpoint, so RevokeAsync
//           calls it rather than being a no-op.
// RELATION: Implements IOAuthFlow. Selected for dropbox by
//           OAuthFlowRegistry. Scopes are declared in the Dropbox App
//           Console for a Scoped App; the authorization request carries
//           no scope parameter. See the M5a handover record.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.OAuth;

/// <summary>The Dropbox OAuth flow.</summary>
internal sealed class DropboxOAuthFlow : IOAuthFlow
{
    private readonly OAuthStateStore _stateStore;
    private readonly Func<string, Task> _openBrowser;
    private readonly Func<int, string, TimeSpan, IOAuthLoopbackListener> _listenerFactory;
    private readonly IOAuthHttpClient _http;
    private readonly TimeSpan _callbackTimeout;

    /// <summary>Creates the flow.</summary>
    public DropboxOAuthFlow(
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

        // Dropbox uses PKCE with S256. No scope parameter: a Scoped App
        // declares its permissions in the Dropbox App Console.
        var url = config.AuthorizationEndpoint
            + "?client_id=" + Uri.EscapeDataString(config.ClientId)
            + "&response_type=code"
            + "&redirect_uri=" + Uri.EscapeDataString(config.RedirectUri)
            + "&state=" + Uri.EscapeDataString(state)
            + "&code_challenge=" + Uri.EscapeDataString(challenge)
            + "&code_challenge_method=S256"
            + "&token_access_type=offline";

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
    public async Task RevokeAsync(
        OAuthClientConfig config,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        // Dropbox provides a real revocation endpoint. Posting to it
        // invalidates the token. A 4xx (rejected or expired token) is
        // permanent and propagates unchanged. A 5xx, a network failure,
        // and every other non-cancellation failure are transient and
        // are wrapped in IOException, which the resilience policy
        // classifies as transient.
        var form = new Dictionary<string, string>
        {
            ["token"] = refreshToken,
        };

        var revokeUrl = "https://api.dropboxapi.com/2/auth/token/revoke";
        try
        {
            await _http.PostFormAsync(revokeUrl, form, cancellationToken).ConfigureAwait(false);
        }
        catch (OAuthHttpStatusException ex) when (ex.StatusCode >= 400 && ex.StatusCode < 500)
        {
            // A 4xx (rejected or expired token) is permanent. Propagate
            // the typed exception unchanged so the caller classifies it
            // permanent. Retrying will not help.
            throw;
        }
        catch (OAuthHttpStatusException ex)
        {
            throw new IOException(
                "Dropbox token revocation failed with a transient status.", ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new IOException("Dropbox token revocation failed.", ex);
        }
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
