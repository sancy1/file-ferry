// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/CloudProviderResolution.cs
// PURPOSE:  The result of resolving a cloud provider by scheme or id. It
//           carries either a resolved ICloudProvider or a safe,
//           credential-free failure reason. The reason is a code, never a
//           message: the resolver maps the code to the user-facing note, so
//           no credential value can travel through this boundary.
// RELATION: Returned by ICloudProviderRegistry.ResolveAsync. Consumed by
//           CloudFileResolver, CloudContentReader, and CloudOutputSink.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// The reason a provider could not be resolved. A code, not a message.
/// </summary>
public enum CloudResolutionFailure
{
    /// <summary>No provider is registered for the scheme.</summary>
    NoProviderRegistered = 0,

    /// <summary>The profile has no stored credential.</summary>
    NoCredential = 1,

    /// <summary>
    /// The profile has more than one stored credential and the path named
    /// none.
    /// </summary>
    MultipleCredentials = 2,

    /// <summary>The path named an instance that is not stored.</summary>
    UnknownInstance = 3,

    /// <summary>
    /// The provider factory threw an unexpected exception. The reason is
    /// safe to show; the exception detail is not.
    /// </summary>
    ProviderConstructionFailed = 4,
}

/// <summary>
/// The result of a provider resolution: a provider, or a safe reason.
/// </summary>
/// <remarks>
/// Exactly one of <see cref="Provider"/> and <see cref="Failure"/> is
/// non-null. Use the factory methods; the constructor is private.
/// </remarks>
public sealed record CloudProviderResolution
{
    private CloudProviderResolution(ICloudProvider? provider, CloudResolutionFailure? failure)
    {
        Provider = provider;
        Failure = failure;
    }

    /// <summary>The resolved provider, or null on failure.</summary>
    public ICloudProvider? Provider { get; }

    /// <summary>The failure reason, or null on success.</summary>
    public CloudResolutionFailure? Failure { get; }

    /// <summary>True when a provider was resolved.</summary>
    public bool IsResolved => Provider is not null;

    /// <summary>Creates a resolved result.</summary>
    /// <param name="provider">The provider. Not null.</param>
    /// <returns>The result.</returns>
    public static CloudProviderResolution Resolved(ICloudProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return new CloudProviderResolution(provider, null);
    }

    /// <summary>Creates a failed result.</summary>
    /// <param name="failure">The reason. Not null.</param>
    /// <returns>The result.</returns>
    public static CloudProviderResolution Failed(CloudResolutionFailure failure)
        => new(null, failure);
}
