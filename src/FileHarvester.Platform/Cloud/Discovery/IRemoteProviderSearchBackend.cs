// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Discovery/IRemoteProviderSearchBackend.cs
// PURPOSE:  The narrow seam between the remote-search resolver and a
//           concrete permitted-API client. The resolver owns caching,
//           rate limiting, and the cloud-search resilience policy; the
//           backend owns the network call and the response mapping.
// RELATION: Implemented by a concrete backend in a later step. Consumed by
//           RemoteProviderSearchResolver. No production backend ships in
//           Step 6; the resolver is not registered in the running app.
// ============================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.Discovery;

/// <summary>
/// A concrete remote provider-search backend: one permitted search API.
/// </summary>
/// <remarks>
/// <para>
/// The backend owns the network call and the mapping from the API response
/// to <see cref="CloudNameCandidate"/>. It is responsible for setting
/// <see cref="CloudNameCandidate.Source"/> to <see cref="CloudNameSource.Remote"/>
/// and for choosing the <see cref="CloudNameConfidence"/> that reflects the
/// strength of the match. The resolver does not re-stamp either field.
/// </para>
/// <para>
/// The interface is name-only. It must never receive a credential, an API
/// key, an access token, a connection string, or any secret. If the API
/// requires authentication, the backend reads the credential from the
/// platform credential store by key name; the key name is never a secret.
/// </para>
/// <para>
/// Exceptions thrown by the backend flow to the resolver. A
/// <c>ResilienceException</c> is converted by the resolver into an empty
/// result; cancellation propagates; any other exception propagates, because
/// a backend defect must not be hidden as a search miss.
/// </para>
/// </remarks>
public interface IRemoteProviderSearchBackend
{
    /// <summary>
    /// Searches the permitted API for provider candidates.
    /// </summary>
    /// <param name="userInput">
    /// The typed cloud name. The resolver has already trimmed it and
    /// guaranteed it is non-empty.
    /// </param>
    /// <param name="cancellationToken">Cancels the network call.</param>
    /// <returns>
    /// The candidates with <see cref="CloudNameSource.Remote"/>. Never null;
    /// may be empty.
    /// </returns>
    Task<IReadOnlyList<CloudNameCandidate>> SearchAsync(
        string userInput,
        CancellationToken cancellationToken = default);
}
