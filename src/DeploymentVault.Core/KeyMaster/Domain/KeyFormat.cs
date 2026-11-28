// ============================================================================
// filepath: src/DeploymentVault.Core/KeyMaster/Domain/KeyFormat.cs
// layer:    Core / Domain
// module:   DeploymentVault.Core.KeyMaster.Domain
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  The output encodings a generated key can take. Raw means the
//           canonical string form for the kind (Base64URL for byte secrets,
//           the charset string for character secrets, PEM for key pairs).
// RELATIONSHIPS
//   Used by    : GenerationSpec, GeneratedKey, FrameworkProfile
//   See also   : KeyKind
// ============================================================================

namespace DeploymentVault.Core.KeyMaster.Domain;

/// <summary>
/// Output encodings for generated keys.
/// </summary>
public enum KeyFormat
{
    /// <summary>The kind's canonical string form (Base64URL for byte secrets).</summary>
    Raw,

    /// <summary>Lowercase hexadecimal.</summary>
    Hex,

    /// <summary>Standard Base64 (with padding).</summary>
    Base64,

    /// <summary>Base64URL (RFC 4648 §5, no padding) — JWT/jose canonical.</summary>
    Base64Url,

    /// <summary>PEM armoring (key pairs only).</summary>
    Pem,

    /// <summary>JSON Web Key (JWK) representation (key pairs and Pro mode).</summary>
    Json,
}
