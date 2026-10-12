// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/OAuth/OAuthFlowRegistry.cs
// PURPOSE:  The per-provider OAuth flow registry. Maps a provider id to its
//           flow. An unknown provider id throws NotSupportedException naming
//           the id.
// RELATION: Implements IOAuthFlowRegistry. Constructed by
//           CloudOAuthServiceCollectionExtensions with the Google and
//           Microsoft flows.
// ============================================================================

using System;
using System.Collections.Generic;

namespace FileHarvester.Platform.Cloud.OAuth;

/// <summary>The per-provider OAuth flow registry.</summary>
internal sealed class OAuthFlowRegistry : IOAuthFlowRegistry
{
    private readonly Dictionary<string, IOAuthFlow> _flows;

    /// <summary>
    /// Creates the registry with the three known provider flows.
    /// </summary>
    /// <param name="googleFlow">The Google Drive flow. Not null.</param>
    /// <param name="microsoftFlow">The Microsoft OneDrive flow. Not null.</param>
    /// <param name="dropboxFlow">The Dropbox flow. Not null.</param>
    public OAuthFlowRegistry(
        IOAuthFlow googleFlow,
        IOAuthFlow microsoftFlow,
        IOAuthFlow dropboxFlow)
    {
        ArgumentNullException.ThrowIfNull(googleFlow);
        ArgumentNullException.ThrowIfNull(microsoftFlow);
        ArgumentNullException.ThrowIfNull(dropboxFlow);

        _flows = new Dictionary<string, IOAuthFlow>(StringComparer.OrdinalIgnoreCase)
        {
            ["gdrive"] = googleFlow,
            ["onedrive"] = microsoftFlow,
            ["dropbox"] = dropboxFlow,
        };
    }

    /// <inheritdoc />
    public IOAuthFlow Resolve(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        if (_flows.TryGetValue(providerId, out var flow))
        {
            return flow;
        }

        throw new NotSupportedException(
            "No OAuth flow is registered for provider [" + providerId + "].");
    }
}
