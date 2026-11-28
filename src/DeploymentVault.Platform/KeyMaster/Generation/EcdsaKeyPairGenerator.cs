// ============================================================================
// filepath: src/DeploymentVault.Platform/KeyMaster/Generation/EcdsaKeyPairGenerator.cs
// layer:    Platform / Generation
// module:   DeploymentVault.Platform.KeyMaster.Generation
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  ECDSA key pair generator for JwtEcdsa (Node) and GoEcdsa (Go).
//           Curves P-256/P-384/P-521 (P-256 default). Exports PEM PKCS#8
//           private + X.509 SPKI public, or JWK JSON (crv/x/y/d).
// RELATIONSHIPS
//   Implements : IKeyGenerator (KeyKind.JwtEcdsa, KeyKind.GoEcdsa)
//   Used by    : KeyGeneratorRegistry
//   See also   : RsaKeyPairGenerator (same export shapes)
// ============================================================================

using System.Security.Cryptography;
using System.Text;
using DeploymentVault.Core.KeyMaster.Abstractions;
using DeploymentVault.Core.KeyMaster.Domain;

namespace DeploymentVault.Platform.KeyMaster.Generation;

/// <summary>
/// Generates ECDSA key pairs on NIST curves with PEM or JWK export.
/// </summary>
public sealed class EcdsaKeyPairGenerator : IKeyGenerator
{
    /// <inheritdoc/>
    public IReadOnlyCollection<KeyKind> Kinds { get; } = new[] { KeyKind.JwtEcdsa, KeyKind.GoEcdsa };

    /// <inheritdoc/>
    public Task<GeneratedKey> GenerateAsync(GenerationSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        var curve = CurveFor(spec.Curve);
        using var ecdsa = ECDsa.Create(curve);

        var privatePem = ecdsa.ExportPkcs8PrivateKeyPem();
        var publicPem = ecdsa.ExportSubjectPublicKeyInfoPem();

        var isJson = spec.Format == KeyFormat.Json;
        var (privateValue, publicValue) = isJson
            ? (JwkPrivate(ecdsa, spec.Curve), JwkPublic(ecdsa, spec.Curve))
            : (privatePem, publicPem);

        // KM-3: record the format actually emitted (Pem or Json), not the request.
        var effectiveFormat = isJson ? KeyFormat.Json : KeyFormat.Pem;
        var crvName = string.IsNullOrEmpty(spec.Curve) ? "P-256" : spec.Curve;
        var fieldBytes = crvName switch { "P-521" => 66, "P-384" => 48, _ => 32 };
        var entropy = EntropyEstimate.FromBytes(fieldBytes);
        return Task.FromResult(new GeneratedKey(
            privateValue, publicValue, spec.Kind, effectiveFormat, fieldBytes,
            entropy.Bits, DateTimeOffset.UtcNow, $"ECDSA-{crvName} · PKCS#8 + SPKI"));
    }

    /// <summary>Resolves a curve name to an ECCurve. Defaults to P-256.</summary>
    public static ECCurve CurveFor(string curve) => curve switch
    {
        "P-384" => ECCurve.NamedCurves.nistP384,
        "P-521" => ECCurve.NamedCurves.nistP521,
        _ => ECCurve.NamedCurves.nistP256,
    };

    private static string JwkPrivate(ECDsa ecdsa, string curve)
    {
        var p = ecdsa.ExportParameters(true);
        var crv = JwkCurve(curve);
        var size = CoordinateSize(crv);
        return "{\"kty\":\"EC\",\"crv\":\"" + crv + "\","
            + "\"x\":\"" + B64UrlPad(p.Q.X!, size) + "\","
            + "\"y\":\"" + B64UrlPad(p.Q.Y!, size) + "\","
            + "\"d\":\"" + B64UrlPad(p.D!, size) + "\"}";
    }

    private static string JwkPublic(ECDsa ecdsa, string curve)
    {
        var p = ecdsa.ExportParameters(false);
        var crv = JwkCurve(curve);
        var size = CoordinateSize(crv);
        return "{\"kty\":\"EC\",\"crv\":\"" + crv + "\","
            + "\"x\":\"" + B64UrlPad(p.Q.X!, size) + "\","
            + "\"y\":\"" + B64UrlPad(p.Q.Y!, size) + "\"}";
    }

    // RFC 7518 §6.2.1.2: EC JWK coordinates are fixed-length big-endian
    // octet strings of ceil(field_size/8) bytes. ExportParameters returns
    // minimal-length integers, so left-pad with zeros.
    private static int CoordinateSize(string crv) => crv switch
    {
        "P-521" => 66,
        "P-384" => 48,
        _ => 32,
    };

    private static string B64UrlPad(byte[] value, int size)
    {
        if (value.Length >= size) return B64Url(value);
        var padded = new byte[size];
        value.CopyTo(padded, size - value.Length);
        return B64Url(padded);
    }

    private static string JwkCurve(string curve) => curve switch
    {
        "P-384" => "P-384",
        "P-521" => "P-521",
        _ => "P-256",
    };

    private static string B64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
