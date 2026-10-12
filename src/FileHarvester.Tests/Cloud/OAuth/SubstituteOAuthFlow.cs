// ============================================================================
// FILE:     src/FileHarvester.Tests/Cloud/OAuth/SubstituteOAuthFlow.cs
// PURPOSE:  A recording test double for IOAuthFlow. It records the config,
//           each call, and the refresh token passed to refresh and revoke,
//           and returns or throws configured outcomes. It never opens a
//           browser, never touches a socket, and never calls a provider.
// RELATION: Used by ConsumerDriveOAuthTests. Not used in production.
// NOTES
//   Every value it returns is an obvious placeholder. No real token, code,
//   or secret appears in any returned value or any exception message.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Platform.Cloud.OAuth;

namespace FileHarvester.Tests.Cloud.OAuth;

/// <summary>A recording IOAuthFlow for consumer-driven OAuth tests.</summary>
internal sealed class SubstituteOAuthFlow : FileHarvester.Platform.Cloud.OAuth.IOAuthFlow
{
    public const string AccessTokenValue = "test-access-token";
    public const string RefreshTokenValue = "test-refresh-token";

    public OAuthClientConfig? LastAuthorizeConfig { get; private set; }
    public OAuthClientConfig? LastRefreshConfig { get; private set; }
    public string? LastRefreshToken { get; private set; }
    public string? LastRevokedToken { get; private set; }
    public int AuthorizeCalls { get; private set; }
    public int RefreshCalls { get; private set; }
    public int RevokeCalls { get; private set; }

    public Func<OAuthClientConfig, Task<OAuthTokens>>? OnAuthorize { get; set; }
    public Func<OAuthClientConfig, string, Task<OAuthTokens>>? OnRefresh { get; set; }
    public Func<OAuthClientConfig, string, Task>? OnRevoke { get; set; }

    public Task<OAuthTokens> AuthorizeAsync(
        OAuthClientConfig config,
        CancellationToken cancellationToken)
    {
        AuthorizeCalls++;
        LastAuthorizeConfig = config;
        if (OnAuthorize is not null) return OnAuthorize(config);
        return Task.FromResult(new OAuthTokens(
            AccessToken: AccessTokenValue,
            ExpiresAtUtc: DateTime.UtcNow.AddHours(1),
            RefreshToken: RefreshTokenValue,
            Scope: null));
    }

    public Task<OAuthTokens> RefreshAsync(
        OAuthClientConfig config,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        RefreshCalls++;
        LastRefreshConfig = config;
        LastRefreshToken = refreshToken;
        if (OnRefresh is not null) return OnRefresh(config, refreshToken);
        return Task.FromResult(new OAuthTokens(
            AccessToken: AccessTokenValue,
            ExpiresAtUtc: DateTime.UtcNow.AddHours(1),
            RefreshToken: null,
            Scope: null));
    }

    public Task RevokeAsync(
        OAuthClientConfig config,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        RevokeCalls++;
        LastRevokedToken = refreshToken;
        if (OnRevoke is not null) return OnRevoke(config, refreshToken);
        return Task.CompletedTask;
    }
}
