// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/OAuth/OAuthExceptions.cs
// PURPOSE:  Domain exceptions for the OAuth authorization flow. All five
//           live in one file so the flow's failure modes are visible in
//           one place. Each carries a message that is safe to log: no
//           token, no code, no verifier, no state, no client secret ever
//           appears in an exception message.
// RELATION: Thrown by OAuthLoopbackListener, OAuthStateStore, and
//           GoogleOAuthFlow. Caught by the flow's caller (C.8 UI) which
//           converts them to user-facing messages.
// ============================================================================

namespace FileHarvester.Platform.Cloud.OAuth;

/// <summary>
/// The authorization flow did not receive a callback within the timeout.
/// </summary>
public sealed class OAuthTimeoutException : Exception
{
    /// <summary>
    /// Creates the exception with the elapsed timeout.
    /// </summary>
    /// <param name="timeout">The timeout that elapsed.</param>
    public OAuthTimeoutException(TimeSpan timeout)
        : base("Authorization timed out after " + timeout.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " seconds.")
    {
        Timeout = timeout;
    }

    /// <summary>The timeout that elapsed.</summary>
    public TimeSpan Timeout { get; }
}

/// <summary>
/// The callback carried a state value that did not match, or that was
/// missing, or that had already been used.
/// </summary>
public sealed class OAuthStateException : Exception
{
    /// <summary>
    /// Creates the exception with a reason.
    /// </summary>
    /// <param name="reason">A short reason. Must not include the state value.</param>
    public OAuthStateException(string reason)
        : base("OAuth state validation failed: " + reason)
    {
    }
}

/// <summary>
/// The user denied the consent request, or the provider returned
/// <c>access_denied</c>.
/// </summary>
public sealed class OAuthConsentDeniedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public OAuthConsentDeniedException()
        : base("The user denied the authorization request.")
    {
    }
}

/// <summary>
/// The callback was malformed: no code, no error, or an unexpected
/// structure. The exception message carries only the provider's error
/// code, never the raw query string.
/// </summary>
public sealed class OAuthCallbackException : Exception
{
    /// <summary>
    /// Creates the exception with the provider error code, if any.
    /// </summary>
    /// <param name="errorCode">
    /// The provider's error code (for example <c>invalid_request</c>), or
    /// null if the callback carried no error code. Never the raw query
    /// string, never any token value.
    /// </param>
    public OAuthCallbackException(string? errorCode)
        : base(errorCode is null
            ? "The authorization callback did not contain a code or an error."
            : "The authorization callback reported an error: " + errorCode)
    {
        ErrorCode = errorCode;
    }

    /// <summary>The provider error code, or null.</summary>
    public string? ErrorCode { get; }
}

/// <summary>
/// The refresh token was rejected by the provider (typically
/// <c>invalid_grant</c>). The user must re-authorize.
/// </summary>
public sealed class OAuthTokenRevokedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public OAuthTokenRevokedException()
        : base("The refresh token is no longer valid. The user must re-authorize.")
    {
    }
}

/// <summary>
/// An OAuth HTTP call returned a non-success status. Carries the
/// numeric status so the caller can classify it as permanent (4xx)
/// or transient (5xx). The message never contains the response body,
/// the request form, a token, or a secret.
/// </summary>
public sealed class OAuthHttpStatusException : Exception
{
    /// <summary>
    /// Creates the exception with the numeric HTTP status.
    /// </summary>
    /// <param name="statusCode">
    /// The HTTP status code. Non-success (400 or greater).
    /// </param>
    public OAuthHttpStatusException(int statusCode)
        : base("OAuth HTTP call returned status " +
               statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".")
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status code.</summary>
    public int StatusCode { get; }
}
