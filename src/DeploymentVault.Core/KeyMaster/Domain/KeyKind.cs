// ============================================================================
// filepath: src/DeploymentVault.Core/KeyMaster/Domain/KeyKind.cs
// layer:    Core / Domain
// module:   DeploymentVault.Core.KeyMaster.Domain
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  The key kinds KeyMaster can generate. Each kind maps to exactly
//           one framework profile (guardrails, minimums, preferred format)
//           and one generator in DeploymentVault.Platform.
// RELATIONSHIPS
//   Used by    : GenerationSpec, GeneratedKey, FrameworkProfile, IKeyGenerator
//   See also   : FrameworkProfile (per-kind guardrail descriptor)
// NOTES
//   - JwtHmac/DotNetHmac/GoHmac/JavaHmac share the symmetric generator but
//     keep distinct kinds so each framework's guardrail stays correct.
//   - JwtRsa and DotNetRsa share the RSA generator; JwtEcdsa and GoEcdsa
//     share the ECDSA generator.
// ============================================================================

namespace DeploymentVault.Core.KeyMaster.Domain;

/// <summary>
/// The framework-specific key kinds KeyMaster generates.
/// </summary>
public enum KeyKind
{
    /// <summary>Node.js JWT symmetric secret (HS256/HS384/HS512).</summary>
    JwtHmac,

    /// <summary>Node.js JWT RSA key pair.</summary>
    JwtRsa,

    /// <summary>Node.js JWT ECDSA key pair.</summary>
    JwtEcdsa,

    /// <summary>Python Django SECRET_KEY.</summary>
    DjangoSecret,

    /// <summary>Python FastAPI secret key.</summary>
    FastApiSecret,

    /// <summary>Python Flask secret key (session or API).</summary>
    FlaskSecret,

    /// <summary>C# / ASP.NET Core JWT HMAC signing key.</summary>
    DotNetHmac,

    /// <summary>C# / ASP.NET Core RSA key pair.</summary>
    DotNetRsa,

    /// <summary>Go JWT HMAC signing key.</summary>
    GoHmac,

    /// <summary>Go AES-GCM payload encryption key (16/24/32 bytes).</summary>
    GoAesGcm,

    /// <summary>Go Ed25519 key pair (PEM PKCS#8 + PKIX).</summary>
    GoEd25519,

    /// <summary>Go ECDSA key pair.</summary>
    GoEcdsa,

    /// <summary>Java (JJWT/Nimbus) HMAC signing key.</summary>
    JavaHmac,

    /// <summary>Universal Pro-mode generator with full charset control.</summary>
    ProKey,
}
