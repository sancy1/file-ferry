// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Discovery/ICloudNameResolver.cs
// PURPOSE:  Resolves a user-typed cloud name to zero or more provider
//           candidates. The name is the only input; no credential ever
//           reaches this interface.
// RELATION: Implemented by the layered resolvers (LocalCatalogResolver,
//           UserProfileResolver, RemoteProviderSearchResolver) and by the
//           composite that chains them. Consumed by the cloud discovery UI.
// ============================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.Discovery;

/// <summary>
/// Resolves a user-typed cloud name to provider candidates.
/// </summary>
/// <remarks>
/// <para>
/// The interface is name-only. It must never receive a credential, an API
/// key, an access token, a connection string, or any secret. A caller that
/// has a credential passes the provider NAME separately; the credential
/// never flows through this interface.
/// </para>
/// <para>
/// A resolver returns candidates in descending confidence. An empty list
/// means no match. An empty or whitespace input returns an empty list.
/// </para>
/// </remarks>
public interface ICloudNameResolver
{
    /// <summary>
    /// Resolves the user input to provider candidates.
    /// </summary>
    /// <param name="userInput">
    /// The typed cloud name. May be a partial name, a misspelling, or an
    /// informal name. Null or whitespace returns an empty list.
    /// </param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>
    /// The candidates, in descending confidence. Never null; may be empty.
    /// </returns>
    Task<IReadOnlyList<CloudNameCandidate>> ResolveAsync(
        string userInput,
        CancellationToken cancellationToken);
}
