// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/AuthKind.cs
// PURPOSE:  The shape of authentication a provider profile requires.
//           LongLivedKey: a static credential the user pastes.
//           OAuth: an interactive consent flow with a refresh token.
//           ConnectionString: a single opaque string that carries
//           the endpoint and the credential together.
// RELATION: Part of ProviderProfile. Consumed by the credential-form
//           engine and by the auth dispatcher in C.6.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// The shape of authentication a provider profile requires.
/// </summary>
/// <remarks>
/// <para>
/// LongLivedKey: the user pastes a static credential (AWS access key,
/// Azure account key, GCS service account JSON).
/// </para>
/// <para>
/// OAuth: the user consents in a browser and the app stores a refresh
/// token. Used by Google Drive, OneDrive, and Dropbox.
/// </para>
/// <para>
/// ConnectionString: a single opaque string that carries endpoint and
/// credential together. Used by some Azure Blob and database-style
/// backends.
/// </para>
/// </remarks>
public enum AuthKind
{
    /// <summary>A static credential the user provides.</summary>
    LongLivedKey = 0,

    /// <summary>An interactive OAuth consent flow.</summary>
    OAuth = 1,

    /// <summary>A single opaque connection string.</summary>
    ConnectionString = 2,
}
