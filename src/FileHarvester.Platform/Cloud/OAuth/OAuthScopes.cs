// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/OAuth/OAuthScopes.cs
// PURPOSE:  Canonical OAuth 2.0 scope constants for the OAuth provider
//           family. Each provider's scopes live here so the minimum-
//           privilege decision is visible in one place and cannot drift
//           between call sites.
// RELATION: Referenced by each provider's OAuth flow configuration
//           (GoogleOAuthFlow in C.6; Microsoft and Dropbox flows in C.7).
//           Not referenced by any provider implementation directly.
// ============================================================================

namespace FileHarvester.Platform.Cloud.OAuth;

/// <summary>
/// OAuth 2.0 scope constants used by the provider flows.
/// </summary>
/// <remarks>
/// <para>
/// Google Drive uses <c>drive.file</c>, not <c>drive</c> and not
/// <c>drive.readonly</c>. The <c>drive.file</c> scope grants access only
/// to files the application creates or the user explicitly opens with the
/// application. It does not grant access to the user's entire Drive.
/// This is the minimum-privilege scope for a file-tool application.
/// </para>
/// <para>
/// Changing a scope is a breaking change: it invalidates existing consent
/// and forces re-authorization. The constants are therefore treated as
/// versioned decisions, not configuration.
/// </para>
/// </remarks>
public static class OAuthScopes
{
    /// <summary>
    /// The scope for Google Drive file-level access. Grants access only to
    /// files the application creates or that the user opens with the
    /// application.
    /// </summary>
    public const string DriveFile = "https://www.googleapis.com/auth/drive.file";
}
