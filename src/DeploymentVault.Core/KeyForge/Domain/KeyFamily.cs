// ============================================================================
// filepath: src/DeploymentVault.Core/KeyForge/Domain/KeyFamily.cs
// layer:    Core / Domain
// module:   DeploymentVault.Core.KeyForge.Domain
// since:    Phase A (KeyForge)
// purpose:  The key-shape families KeyForge recognizes. Each family has its
//           own detection regex and its own shape-preserving fake generator.
// RELATIONSHIPS
//   Used by    : KeySpan, DetectedKey, IKeyExtractor, IKeyForger
//   See also   : ShapePreservingKeyForger (the per-family fake generator)
// NOTES
//   - PrefixedToken: gsk_..., sk-..., pk_live_..., AIza... (lowercase prefix
//     + separator + alphanumeric body).
//   - Jwt: three base64url segments separated by dots.
//   - Base64Blob: 40+ base64 chars, no separators, optional = padding.
//   - Uuid: canonical 8-4-4-4-12 hex with hyphens.
//   - PemBlock: -----BEGIN ... PRIVATE KEY----- ... -----END ... -----.
//   - Generic: fallback for key-shaped strings that match no family.
// ============================================================================

namespace DeploymentVault.Core.KeyForge.Domain;

/// <summary>
/// The recognized key-shape families.
/// </summary>
public enum KeyFamily
{
    /// <summary>A prefixed token such as gsk_..., sk-..., pk_live_..., AIza....</summary>
    PrefixedToken,

    /// <summary>A JSON Web Token (three base64url segments separated by dots).</summary>
    Jwt,

    /// <summary>A long base64 blob with no separators.</summary>
    Base64Blob,

    /// <summary>A canonical UUID (8-4-4-4-12 hex with hyphens).</summary>
    Uuid,

    /// <summary>A PEM private-key block.</summary>
    PemBlock,

    /// <summary>A key-shaped string that matches no specific family.</summary>
    Generic,
}
