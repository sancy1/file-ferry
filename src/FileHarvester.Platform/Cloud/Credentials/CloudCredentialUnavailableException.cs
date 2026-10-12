// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Credentials/CloudCredentialUnavailableException.cs
// PURPOSE:  Thrown when a cloud credential operation is attempted on a
//           platform whose credential backend is not yet implemented.
//           Names the platform, the missing backend, and the release
//           that will deliver it.
// RELATION: Thrown by MacOsKeychainStore, LinuxSecretServiceStore, and
//           NotSupportedCredentialStore. Caught by the cloud UI to
//           display a specific, actionable message.
// ============================================================================

namespace FileHarvester.Platform.Cloud.Credentials;

/// <summary>
/// Thrown when a cloud credential operation cannot proceed because the
/// current platform's credential backend is not implemented in this
/// release of File-Ferry.
/// </summary>
/// <remarks>
/// <para>
/// This is a deliberate, named, actionable failure. It is not a generic
/// "not implemented" error. The message names the platform, the
/// required backend, and the release that will deliver it. Local file
/// features are unaffected and continue to work on this platform.
/// </para>
/// </remarks>
public sealed class CloudCredentialUnavailableException : Exception
{
    /// <summary>
    /// Creates the exception with a specific message.
    /// </summary>
    /// <param name="platformName">
    /// The platform name (for example, "macOS" or "Linux").
    /// </param>
    /// <param name="requiredBackend">
    /// The credential backend the platform requires (for example,
    /// "macOS Keychain" or "Linux Secret Service (libsecret)").
    /// </param>
    /// <param name="scheduledPhase">
    /// The phase that will deliver the backend (for example,
    /// "Platform Enablement").
    /// </param>
    public CloudCredentialUnavailableException(
        string platformName,
        string requiredBackend,
        string scheduledPhase)
        : base(BuildMessage(platformName, requiredBackend, scheduledPhase))
    {
        PlatformName = platformName;
        RequiredBackend = requiredBackend;
        ScheduledPhase = scheduledPhase;
    }

    /// <summary>The platform name, such as "macOS".</summary>
    public string PlatformName { get; }

    /// <summary>The credential backend the platform requires.</summary>
    public string RequiredBackend { get; }

    /// <summary>The phase that will deliver the backend.</summary>
    public string ScheduledPhase { get; }

    private static string BuildMessage(
        string platformName,
        string requiredBackend,
        string scheduledPhase)
    {
        return "Cloud authentication is not yet supported on " + platformName +
               " in this release of File-Ferry. The required backend, " +
               requiredBackend + ", is scheduled for the " + scheduledPhase +
               " phase. Local file features continue to work on this platform.";
    }
}
