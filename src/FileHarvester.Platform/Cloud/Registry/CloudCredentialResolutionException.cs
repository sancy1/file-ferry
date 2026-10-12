// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Registry/CloudCredentialResolutionException.cs
// PURPOSE:  The typed exception ProviderConstruction throws when it cannot
//           resolve a credential instance. It carries a
//           CloudResolutionFailure reason so the registry can map it to a
//           user-facing note without inspecting message text. The message
//           is developer-facing; the user never sees it.
// RELATION: Thrown by ProviderConstruction. Caught by CloudProviderRegistry,
//           which maps Reason to CloudProviderResolution.Failed.
// ============================================================================

using System;
using FileHarvester.Core.Domain;

namespace FileHarvester.Platform.Cloud.Registry;

/// <summary>
/// Raised when a credential instance cannot be resolved. Carries the
/// safe failure reason.
/// </summary>
/// <remarks>
/// The exception message is developer-facing and must never be shown to
/// the user. It must never contain a credential value, a secret, a token,
/// an account id, or a connection string. The user-facing note is derived
/// from the reason by the resolver.
/// </remarks>
internal sealed class CloudCredentialResolutionException : Exception
{
    /// <summary>
    /// Creates the exception with a reason and a developer-facing message.
    /// </summary>
    /// <param name="reason">The safe failure reason.</param>
    /// <param name="message">The developer-facing message.</param>
    public CloudCredentialResolutionException(
        CloudResolutionFailure reason,
        string message)
        : base(message)
    {
        Reason = reason;
    }

    /// <summary>The safe failure reason.</summary>
    public CloudResolutionFailure Reason { get; }
}
