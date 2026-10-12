// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/OAuth/HttpOAuthHttpClient.cs
// PURPOSE:  The live IOAuthHttpClient. Posts form-encoded parameters to the
//           provider token endpoint and returns the response body. Used by
//           MicrosoftOAuthFlow for the code exchange and the refresh.
// RELATION: Implements IOAuthHttpClient. Registered by
//           CloudOAuthServiceCollectionExtensions. Tests substitute the
//           interface and never reach this class.
// ============================================================================

using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.OAuth;

/// <summary>The live HTTP POST seam for the OAuth token endpoints.</summary>
internal sealed class HttpOAuthHttpClient : IOAuthHttpClient
{
    private static readonly HttpClient Client = new();

    /// <inheritdoc />
    public async Task<string> PostFormAsync(
        string url,
        IReadOnlyDictionary<string, string> form,
        CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await Client.PostAsync(url, content, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new OAuthHttpStatusException((int)response.StatusCode);
        }

        return body;
    }
}
