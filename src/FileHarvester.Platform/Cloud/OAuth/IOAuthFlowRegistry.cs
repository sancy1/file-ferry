// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/OAuth/IOAuthFlowRegistry.cs
// PURPOSE:  Selects the OAuth flow for a provider id. Google Drive uses the
//           Google flow; OneDrive uses the Microsoft flow. An unknown
//           provider id throws NotSupportedException naming the id. The
//           exception message carries no credential, token, or secret.
// RELATION: Implemented by OAuthFlowRegistry. Consumed by
//           CloudConnectionService. The registry keeps each flow unaware of
//           the others.
// ============================================================================

namespace FileHarvester.Platform.Cloud.OAuth;

/// <summary>Selects the OAuth flow for a provider id.</summary>
internal interface IOAuthFlowRegistry
{
    /// <summary>
    /// Resolves the flow for the given provider id.
    /// </summary>
    /// <param name="providerId">
    /// The provider id (for example gdrive or onedrive). Non-null.
    /// </param>
    /// <returns>The flow for that provider.</returns>
    /// <exception cref="System.NotSupportedException">
    /// No flow is registered for the provider id. The message names the id
    /// and contains no credential value.
    /// </exception>
    IOAuthFlow Resolve(string providerId);
}
