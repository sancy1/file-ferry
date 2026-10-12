// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/OAuth/IOAuthFlow.cs
// PURPOSE:  The provider-agnostic OAuth 2.0 flow seam. Every OAuth
//           provider — Google Drive in C.6, Microsoft and Dropbox in
//           C.7 — implements this interface. It is storage-independent
//           and UI-independent: it does not know about ICredentialStore,
//           the durable refresh token, or any dialog.
// RELATION: Implemented by GoogleOAuthFlow (C.6.3) and by the C.7 flows.
//           Consumed by OAuthTokenStore (C.6.4) which owns the durable
//           credential and the delete-after-revoke step.
// ============================================================================

using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.OAuth;

/// <summary>
/// An OAuth 2.0 authorization flow for one provider.
/// </summary>
/// <remarks>
/// <para>
/// The interface is storage-independent and UI-independent. It does not
/// read or write the credential store; the durable refresh token is the
/// caller's responsibility. It does not depend on any dialog; the flow
/// opens the system browser internally. It is not pure: AuthorizeAsync
/// opens a browser, binds a loopback listener, and calls the provider's
/// authorization and token endpoints.
/// </para>
/// <para>
/// <b>Cancellation contract.</b> When the caller's
/// <paramref name="cancellationToken"/> fires, the method throws
/// <see cref="System.OperationCanceledException"/> unchanged. It is never
/// wrapped in a domain exception and never converted to a timeout.
/// </para>
/// <para>
/// <b>Failure contract.</b> User denial throws
/// <see cref="OAuthConsentDeniedException"/>. A malformed or error-bearing
/// callback throws <see cref="OAuthCallbackException"/>. A state mismatch
/// throws <see cref="OAuthStateException"/>. The loopback timeout throws
/// <see cref="OAuthTimeoutException"/>. A refresh rejected with
/// <c>invalid_grant</c> throws <see cref="OAuthTokenRevokedException"/>.
/// No exception message ever contains a token, a code, a verifier, or a
/// state value.
/// </para>
/// </remarks>
public interface IOAuthFlow
{
    /// <summary>
    /// Runs the full authorization flow: opens the system browser, waits
    /// for the loopback callback, exchanges the code for tokens, and
    /// returns them.
    /// </summary>
    /// <param name="config">
    /// The non-secret client registration. Must not be null.
    /// </param>
    /// <param name="cancellationToken">Cancels the flow.</param>
    /// <returns>
    /// The tokens from the authorization. The refresh token is non-null
    /// on a first authorization.
    /// </returns>
    /// <exception cref="OAuthConsentDeniedException">
    /// The user denied the consent request.
    /// </exception>
    /// <exception cref="OAuthCallbackException">
    /// The callback was malformed or carried an error code.
    /// </exception>
    /// <exception cref="OAuthStateException">
    /// The returned state did not match, or had already been consumed.
    /// </exception>
    /// <exception cref="OAuthTimeoutException">
    /// The callback did not arrive within the listener's timeout.
    /// </exception>
    /// <exception cref="System.OperationCanceledException">
    /// The caller cancelled. Rethrown unchanged.
    /// </exception>
    Task<OAuthTokens> AuthorizeAsync(
        OAuthClientConfig config,
        CancellationToken cancellationToken);

    /// <summary>
    /// Exchanges a refresh token for a new access token.
    /// </summary>
    /// <param name="config">The non-secret client registration.</param>
    /// <param name="refreshToken">
    /// The durable refresh token. Supplied by the caller, never read from
    /// a store by the flow.
    /// </param>
    /// <param name="cancellationToken">Cancels the refresh.</param>
    /// <returns>
    /// The refreshed tokens. The result's RefreshToken is the rotated
    /// value if the provider rotated it, or null if it did not — in which
    /// case the caller preserves the existing refresh token.
    /// </returns>
    /// <exception cref="OAuthTokenRevokedException">
    /// The provider rejected the refresh token (typically invalid_grant).
    /// The user must re-authorize.
    /// </exception>
    /// <exception cref="System.OperationCanceledException">
    /// The caller cancelled. Rethrown unchanged.
    /// </exception>
    Task<OAuthTokens> RefreshAsync(
        OAuthClientConfig config,
        string refreshToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revokes the refresh token at the provider.
    /// </summary>
    /// <remarks>
    /// The flow revokes the token only. It does not delete the durable
    /// credential: the orchestration layer deletes the stored refresh
    /// token after a successful revocation. If revocation fails, the
    /// durable token is preserved — the flow never silently discards a
    /// credential it cannot revoke.
    /// </remarks>
    /// <param name="config">The non-secret client registration.</param>
    /// <param name="refreshToken">The refresh token to revoke.</param>
    /// <param name="cancellationToken">Cancels the revocation.</param>
    /// <exception cref="System.OperationCanceledException">
    /// The caller cancelled. Rethrown unchanged.
    /// </exception>
    Task RevokeAsync(
        OAuthClientConfig config,
        string refreshToken,
        CancellationToken cancellationToken);
}
