// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Discovery/RemoteProviderSearchValidator.cs
// PURPOSE:  A pure validator for the remote-search options/backend/endpoint
//           combination. No reflection, no DI, no service provider. The
//           caller states whether a backend is registered and whether that
//           backend requires an endpoint; the validator decides whether the
//           options satisfy them.
// RELATION: Consumed by a startup call site in a later step, when a
//           concrete backend and a configuration source exist. Not wired
//           into the running app in Step 6 (F-090).
// ============================================================================

using System.Collections.Generic;

namespace FileHarvester.Platform.Cloud.Discovery;

/// <summary>
/// Validates a remote-search configuration against the state of the DI
/// graph at startup.
/// </summary>
public static class RemoteProviderSearchValidator
{
    /// <summary>
    /// Validates the options against the backend state.
    /// </summary>
    /// <param name="options">The options. Not null.</param>
    /// <param name="backendRegistered">
    /// True when the caller has registered an IRemoteProviderSearchBackend
    /// descriptor. The caller inspects the actual service collection; this
    /// parameter is not a guess.
    /// </param>
    /// <param name="endpointRequired">
    /// True when the registered backend declares that it requires an
    /// endpoint. False when no backend is registered or when the registered
    /// backend does not require one.
    /// </param>
    /// <returns>
    /// The error messages. Empty when the configuration is valid. Never null.
    /// </returns>
    public static IReadOnlyList<string> Validate(
        RemoteProviderSearchOptions options,
        bool backendRegistered,
        bool endpointRequired)
    {
        if (!options.Enabled)
        {
            return System.Array.Empty<string>();
        }

        var errors = new List<string>();

        if (!backendRegistered)
        {
            errors.Add(
                "Remote provider search is enabled but no backend is registered.");
        }
        else if (endpointRequired && options.Endpoint is null)
        {
            errors.Add(
                "Remote provider search is enabled but no endpoint is configured.");
        }

        return errors;
    }
}
