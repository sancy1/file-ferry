// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/OAuth/OAuthTokens.cs
// PURPOSE:  The result of a successful OAuth authorization or refresh.
//           Carries the access token, its expiry, the refresh token, and
//           the granted scope. The refresh token is the durable value and
//           is written to ICredentialStore; the access token is held in
//           memory only and is never persisted.
// RELATION: Produced by IOAuthFlow.AuthorizeAsync and .RefreshAsync.
//           Consumed by OAuthTokenStore, which splits the durable value
//           (refresh token) from the ephemeral one (access token).
// ============================================================================

using System;

namespace FileHarvester.Platform.Cloud.OAuth;

/// <summary>
/// The result of an OAuth authorization or refresh.
/// </summary>
/// <param name="AccessToken">
/// The access token. Not persisted. Non-null on success.
/// </param>
/// <param name="ExpiresAtUtc">
/// The instant the access token expires, in UTC. Computed by the flow
/// from the token response's ExpiresInSeconds and the issue time.
/// </param>
/// <param name="RefreshToken">
/// The refresh token. The durable credential. Nullable because a refresh
/// response does not always carry a new refresh token (the old one remains
/// valid); the first authorization always carries one.
/// </param>
/// <param name="Scope">
/// The granted scope string, space-separated, as returned by the provider.
/// May differ from the requested scope if the user granted a subset.
/// Nullable because some refresh responses omit it.
/// </param>
public sealed record OAuthTokens(
    string AccessToken,
    DateTime ExpiresAtUtc,
    string? RefreshToken,
    string? Scope);
