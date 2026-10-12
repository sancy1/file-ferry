// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/OAuth/OAuthClientConfig.cs
// PURPOSE:  Non-secret OAuth 2.0 client registration for one provider.
//           Carries the public client id, the authorization and token
//           endpoints, the requested scopes, and the loopback redirect
//           URI. It never carries a client secret: desktop OAuth uses
//           PKCE and a public client.
// RELATION: Produced by OAuthClientConfigStore (read from
//           %APPDATA%\FileHarvester\oauth-clients.json) and consumed by
//           IOAuthFlow implementations. GoogleOAuthFlow in C.6; Microsoft
//           and Dropbox flows in C.7.
// ============================================================================

using System.Collections.Generic;

namespace FileHarvester.Platform.Cloud.OAuth;

/// <summary>
/// Non-secret OAuth 2.0 client registration for one provider.
/// </summary>
/// <param name="ProviderId">
/// Stable identifier for the provider (for example <c>google-drive</c>).
/// Used as the credential-store key prefix.
/// </param>
/// <param name="ClientId">
/// The public OAuth client id issued by the provider. Not a secret.
/// </param>
/// <param name="AuthorizationEndpoint">
/// The authorization endpoint URL (for Google:
/// <c>https://accounts.google.com/o/oauth2/v2/auth</c>).
/// </param>
/// <param name="TokenEndpoint">
/// The token endpoint URL (for Google:
/// <c>https://oauth2.googleapis.com/token</c>).
/// </param>
/// <param name="Scopes">
/// The requested OAuth scopes. Never null. Must contain at least one scope.
/// </param>
/// <param name="RedirectUri">
/// The loopback redirect URI registered with the provider (for example
/// <c>http://127.0.0.1:53682/callback/</c>). Must match the registered
/// URI exactly.
/// </param>
public sealed record OAuthClientConfig(
    string ProviderId,
    string ClientId,
    string AuthorizationEndpoint,
    string TokenEndpoint,
    IReadOnlyList<string> Scopes,
    string RedirectUri)
{
    /// <summary>
    /// The reserved credential-store instance name under which a
    /// provider's OAuth client secret is stored. A user-chosen instance
    /// name cannot equal this value: the credential form rejects it as
    /// reserved.
    /// </summary>
    public const string ClientSecretInstanceName = "__client_secret__";
}
