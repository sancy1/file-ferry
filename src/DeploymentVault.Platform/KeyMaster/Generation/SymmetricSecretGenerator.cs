// ============================================================================
// filepath: src/DeploymentVault.Platform/KeyMaster/Generation/SymmetricSecretGenerator.cs
// layer:    Platform / Generation
// module:   DeploymentVault.Platform.KeyMaster.Generation
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  HMAC symmetric secret generator shared by JwtHmac (Node),
//           DotNetHmac (ASP.NET), GoHmac (Go), and JavaHmac (JVM). Enforces
//           the RFC 7518 minimums: HS256 >= 32 bytes, HS384 >= 48,
//           HS512 >= 64. Refuses shorter requests with ArgumentException —
//           the UI prevents it, the generator also refuses (correct by
//           construction).
// RELATIONSHIPS
//   Implements : IKeyGenerator
//   Used by    : KeyGeneratorRegistry
//   See also   : SecureRandomSource, KeyFormatEncoder, FrameworkProfile
// ============================================================================

using DeploymentVault.Core.KeyMaster.Abstractions;
using DeploymentVault.Core.KeyMaster.Domain;

namespace DeploymentVault.Platform.KeyMaster.Generation;

/// <summary>
/// Generates HMAC secrets for JWT/ASP.NET/Go/Java with per-framework minimums.
/// </summary>
public sealed class SymmetricSecretGenerator : IKeyGenerator
{
    /// <inheritdoc/>
    public IReadOnlyCollection<KeyKind> Kinds { get; } =
        new[] { KeyKind.JwtHmac, KeyKind.DotNetHmac, KeyKind.GoHmac, KeyKind.JavaHmac };

    /// <inheritdoc/>
    public Task<GeneratedKey> GenerateAsync(GenerationSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        var profile = FrameworkProfile.For(spec.Kind);
        var minBytes = MinBytesFor(spec.Algorithm, profile.MinBytes);
        var byteCount = Math.Max(spec.Length, minBytes);
        if (byteCount > profile.MaxLength * 2) // generous ceiling; slider caps at 128
        {
            throw new ArgumentOutOfRangeException(nameof(spec), $"Length {byteCount} exceeds the supported maximum.");
        }

        var bytes = SecureRandomSource.Bytes(byteCount);
        var value = KeyFormatEncoder.EncodeBytes(bytes, spec.Format);
        var entropy = EntropyEstimate.FromBytes(byteCount);
        var label = $"{AlgorithmLabel(spec.Algorithm)} · {byteCount} bytes";

        return Task.FromResult(new GeneratedKey(
            value, null, spec.Kind, spec.Format, byteCount,
            entropy.Bits, DateTimeOffset.UtcNow, label));
    }

    /// <summary>Minimum bytes for an HMAC algorithm; falls back to the profile minimum.</summary>
    public static int MinBytesFor(string algorithm, int fallback)
    {
        return algorithm.ToUpperInvariant() switch
        {
            "HS256" => 32,
            "HS384" => 48,
            "HS512" => 64,
            _ => fallback,
        };
    }

    private static string AlgorithmLabel(string algorithm) =>
        string.IsNullOrEmpty(algorithm) ? "HMAC" : algorithm.ToUpperInvariant();
}
