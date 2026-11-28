// ============================================================================
// filepath: src/DeploymentVault.Platform/KeyMaster/Generation/RsaKeyPairGenerator.cs
// layer:    Platform / Generation
// module:   DeploymentVault.Platform.KeyMaster.Generation
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  RSA key pair generator for JwtRsa (Node) and DotNetRsa (ASP.NET).
//           Modulus 2048/3072/4096 (2048 minimum enforced). Exports:
//           PEM PKCS#8 private + X.509 SPKI public (default), PEM PKCS#1
//           private (legacy), or JWK JSON for both halves.
// RELATIONSHIPS
//   Implements : IKeyGenerator (KeyKind.JwtRsa, KeyKind.DotNetRsa)
//   Used by    : KeyGeneratorRegistry
//   See also   : SecureRandomSource (not used — RSA uses OS CSPRNG internally)
// ============================================================================

using System.Security.Cryptography;
using System.Text;
using DeploymentVault.Core.KeyMaster.Abstractions;
using DeploymentVault.Core.KeyMaster.Domain;

namespace DeploymentVault.Platform.KeyMaster.Generation;

/// <summary>
/// Generates RSA key pairs with PEM or JWK export.
/// </summary>
public sealed class RsaKeyPairGenerator : IKeyGenerator
{
    /// <inheritdoc/>
    public IReadOnlyCollection<KeyKind> Kinds { get; } = new[] { KeyKind.JwtRsa, KeyKind.DotNetRsa };

    /// <inheritdoc/>
    public Task<GeneratedKey> GenerateAsync(GenerationSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        var bits = spec.ModulusBits is 2048 or 3072 or 4096 ? spec.ModulusBits : 2048;
        if (bits < 2048)
        {
            throw new ArgumentException("RSA modulus must be at least 2048 bits.", nameof(spec));
        }

        using var rsa = RSA.Create(bits);
        var privatePem = rsa.ExportPkcs8PrivateKeyPem();
        var publicPem = rsa.ExportSubjectPublicKeyInfoPem();

        var isJson = spec.Format == KeyFormat.Json;
        var isPkcs1 = spec.Format == KeyFormat.Pem && IsPkcs1(spec);
        var (privateValue, publicValue) = isJson
            ? (JwkPrivate(rsa), JwkPublic(rsa))
            : isPkcs1
                ? (Pkcs1Private(rsa), publicPem)
                : (privatePem, publicPem);

        // KM-3: record the format actually emitted (Pem or Json), not the request.
        var effectiveFormat = isJson ? KeyFormat.Json : KeyFormat.Pem;
        var entropy = EntropyEstimate.FromBytes(bits / 8);
        return Task.FromResult(new GeneratedKey(
            privateValue, publicValue, spec.Kind, effectiveFormat, bits / 8,
            entropy.Bits, DateTimeOffset.UtcNow, $"RSA-{bits} · PKCS#8 + SPKI"));
    }

    private static bool IsPkcs1(GenerationSpec spec) =>
        string.Equals(spec.Algorithm, "Pkcs1", StringComparison.OrdinalIgnoreCase);

    private static string Pkcs1Private(RSA rsa)
    {
        var der = rsa.ExportRSAPrivateKey();
        return "-----BEGIN RSA PRIVATE KEY-----\n"
            + Convert.ToBase64String(der, Base64FormattingOptions.InsertLineBreaks)
            + "\n-----END RSA PRIVATE KEY-----\n";
    }

    private static string JwkPrivate(RSA rsa)
    {
        var p = rsa.ExportParameters(true);
        var sb = new StringBuilder("{");
        sb.Append("\"kty\":\"RSA\",");
        sb.Append("\"n\":\"").Append(B64Url(p.Modulus!)).Append("\",");
        sb.Append("\"e\":\"").Append(B64Url(p.Exponent!)).Append("\",");
        sb.Append("\"d\":\"").Append(B64Url(p.D!)).Append("\",");
        sb.Append("\"p\":\"").Append(B64Url(p.P!)).Append("\",");
        sb.Append("\"q\":\"").Append(B64Url(p.Q!)).Append("\",");
        sb.Append("\"dp\":\"").Append(B64Url(p.DP!)).Append("\",");
        sb.Append("\"dq\":\"").Append(B64Url(p.DQ!)).Append("\",");
        sb.Append("\"qi\":\"").Append(B64Url(p.InverseQ!)).Append('"');
        sb.Append('}');
        return sb.ToString();
    }

    private static string JwkPublic(RSA rsa)
    {
        var p = rsa.ExportParameters(false);
        return "{\"kty\":\"RSA\",\"n\":\"" + B64Url(p.Modulus!)
            + "\",\"e\":\"" + B64Url(p.Exponent!) + "\"}";
    }

    private static string B64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
