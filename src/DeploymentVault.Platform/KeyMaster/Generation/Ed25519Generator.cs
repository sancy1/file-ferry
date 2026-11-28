// ============================================================================
// filepath: src/DeploymentVault.Platform/KeyMaster/Generation/Ed25519Generator.cs
// layer:    Platform / Generation
// module:   DeploymentVault.Platform.KeyMaster.Generation
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  Go Ed25519 key pair generator. Uses the pure-C# Ed25519Math
//           primitive (no external package — offline constraint). Exports
//           PEM PKCS#8 private + PKIX public (default), raw hex, or JWK OKP.
// RELATIONSHIPS
//   Implements : IKeyGenerator (KeyKind.GoEd25519)
//   Used by    : KeyGeneratorRegistry
//   See also   : Ed25519Math (RFC 8032 primitives)
// ============================================================================

using DeploymentVault.Core.KeyMaster.Abstractions;
using DeploymentVault.Core.KeyMaster.Domain;

namespace DeploymentVault.Platform.KeyMaster.Generation;

/// <summary>
/// Generates Ed25519 key pairs with PEM, hex, or JWK export.
/// </summary>
public sealed class Ed25519Generator : IKeyGenerator
{
    // PKCS#8 private prefix: SEQUENCE { INTEGER 0, SEQUENCE { OID 1.3.101.112 }, OCTET STRING { OCTET STRING { seed } } }
    private static readonly byte[] Pkcs8Prefix =
    {
        0x30, 0x2e, 0x02, 0x01, 0x00, 0x30, 0x05, 0x06, 0x03, 0x2b, 0x65, 0x70,
        0x04, 0x22, 0x04, 0x20,
    };

    // PKIX public prefix: SEQUENCE { SEQUENCE { OID 1.3.101.112 }, BIT STRING { pubkey } }
    private static readonly byte[] PkixPrefix =
    {
        0x30, 0x2a, 0x30, 0x05, 0x06, 0x03, 0x2b, 0x65, 0x70, 0x03, 0x21, 0x00,
    };

    /// <inheritdoc/>
    public IReadOnlyCollection<KeyKind> Kinds { get; } = new[] { KeyKind.GoEd25519 };

    /// <inheritdoc/>
    public Task<GeneratedKey> GenerateAsync(GenerationSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        var (seed, publicKey) = Ed25519Math.GenerateKeyPair();

        var (privateValue, publicValue) = spec.Format switch
        {
            KeyFormat.Json => (Jwk(seed, publicKey), JwkPublicOnly(publicKey)),
            KeyFormat.Hex => (Convert.ToHexString(seed).ToLowerInvariant(),
                              Convert.ToHexString(publicKey).ToLowerInvariant()),
            _ => (PemPrivate(seed), PemPublic(publicKey)),
        };

        // KM-3: record the format actually emitted, not the requested one.
        var effectiveFormat = spec.Format is KeyFormat.Json or KeyFormat.Hex ? spec.Format : KeyFormat.Pem;
        var entropy = EntropyEstimate.FromBytes(32);
        return Task.FromResult(new GeneratedKey(
            privateValue, publicValue, spec.Kind, effectiveFormat, 32,
            entropy.Bits, DateTimeOffset.UtcNow, "Ed25519 · PKCS#8 + PKIX"));
    }

    private static string PemPrivate(byte[] seed)
    {
        var der = new byte[Pkcs8Prefix.Length + seed.Length];
        Pkcs8Prefix.CopyTo(der, 0);
        seed.CopyTo(der, Pkcs8Prefix.Length);
        return Pem("PRIVATE KEY", der);
    }

    private static string PemPublic(byte[] publicKey)
    {
        var der = new byte[PkixPrefix.Length + publicKey.Length];
        PkixPrefix.CopyTo(der, 0);
        publicKey.CopyTo(der, PkixPrefix.Length);
        return Pem("PUBLIC KEY", der);
    }

    private static string Pem(string label, byte[] der) =>
        $"-----BEGIN {label}-----\n"
        + Convert.ToBase64String(der, Base64FormattingOptions.InsertLineBreaks)
        + $"\n-----END {label}-----\n";

    private static string Jwk(byte[] seed, byte[] publicKey) =>
        "{\"kty\":\"OKP\",\"crv\":\"Ed25519\",\"x\":\"" + B64Url(publicKey)
        + "\",\"d\":\"" + B64Url(seed) + "\"}";

    private static string JwkPublicOnly(byte[] publicKey) =>
        "{\"kty\":\"OKP\",\"crv\":\"Ed25519\",\"x\":\"" + B64Url(publicKey) + "\"}";

    private static string B64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
