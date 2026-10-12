// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/OAuth/IOAuthHttpClient.cs
// PURPOSE:  The mockable seam over the HTTP calls the Microsoft OAuth flow
//           makes: the token exchange and the refresh. The live
//           implementation posts to the provider endpoints; tests
//           substitute this interface and return controlled JSON.
// RELATION: Consumed by MicrosoftOAuthFlow. Implemented by
//           HttpOAuthHttpClient.
// ============================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.OAuth;

/// <summary>A minimal HTTP POST seam for the OAuth token endpoints.</summary>
internal interface IOAuthHttpClient
{
    /// <summary>
    /// Posts form-encoded parameters to a URL and returns the response body.
    /// </summary>
    /// <param name="url">The endpoint URL. Non-null.</param>
    /// <param name="form">The form parameters. Non-null.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>The response body as a string.</returns>
    /// <exception cref="OAuthHttpStatusException">
    /// The call returned a non-success HTTP status. The exception
    /// carries the numeric status; the message never contains the
    /// response body, the form, a token, or a secret.
    /// </exception>
    Task<string> PostFormAsync(
        string url,
        IReadOnlyDictionary<string, string> form,
        CancellationToken ct);
}
