// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Credentials/CredentialKind.cs
// PURPOSE:  The kind of credential a CredentialStoreKey addresses. Static
//           credentials and OAuth refresh tokens use distinct namespaces
//           in the platform credential store so a listing, read, or delete
//           for one kind cannot reach a credential of the other kind.
// RELATION: Part of CredentialStoreKey. Consumed by ITypedCredentialStore
//           implementations and by the OAuth token store. CredentialKey
//           is not affected by this type; its two-argument record shape
//           and its static store-key format are unchanged.
// ============================================================================

namespace FileHarvester.Platform.Cloud.Credentials;

/// <summary>
/// The kind of credential stored under a <c>CredentialStoreKey</c>.
/// </summary>
public enum CredentialKind
{
    /// <summary>A static credential (API key, key pair, connection string).</summary>
    Static = 0,

    /// <summary>A durable OAuth refresh token.</summary>
    OAuth = 1,
}
