// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Discovery/IRemoteProviderSearch.cs
// PURPOSE:  The opt-in remote provider-name search. Separate from
//           ICloudNameResolver so that no existing caller reaches it
//           implicitly. The name is the only input; no credential ever
//           reaches this interface.
// RELATION: Implemented by RemoteProviderSearchResolver. Invoked only by a
//           caller that explicitly asks for a remote search. Not registered
//           or invoked by the running app in Step 6 (F-088).
// ============================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.Discovery;

/// <summary>
/// The opt-in remote provider-name search.
/// </summary>
/// <remarks>
/// <para>
/// This interface is intentionally separate from <see cref="ICloudNameResolver"/>.
/// The local resolver path never reaches a remote search; a caller that wants
/// a remote search resolves <see cref="IRemoteProviderSearch"/> directly and
/// invokes it explicitly.
/// </para>
/// <para>
/// The interface is name-only. It must never receive a credential, an API
/// key, an access token, a connection string, or any secret.
/// </para>
/// <para>
/// Failure contract:
/// blank input returns an empty list without calling the backend;
/// a resilience failure (timeout or circuit-open) returns an empty list;
/// cancellation propagates to the caller;
/// any other backend exception propagates - a backend defect is not a
/// search miss and must not be hidden as one.
/// </para>
/// </remarks>
public interface IRemoteProviderSearch
{
    /// <summary>
    /// Searches remotely for provider candidates matching the user input.
    /// </summary>
    /// <param name="userInput">
    /// The typed cloud name. Null or whitespace returns an empty list
    /// without contacting the backend.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the search. Cancellation propagates; it is never converted
    /// into an empty result.
    /// </param>
    /// <returns>
    /// The candidates, in descending confidence. Never null; may be empty.
    /// </returns>
    Task<IReadOnlyList<CloudNameCandidate>> SearchAsync(
        string userInput,
        CancellationToken cancellationToken = default);
}
