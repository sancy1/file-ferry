// ============================================================================
// filepath: src/DeploymentVault.Platform/KeyMaster/Generation/FastApiSecretGenerator.cs
// layer:    Platform / Generation
// module:   DeploymentVault.Platform.KeyMaster.Generation
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  FastAPI secret generator. Produces the secrets.token_urlsafe
//           equivalent: 32 random bytes encoded as URL-safe Base64 (no +/=),
//           safe to store in .env without quoting. Minimum 32 bytes enforced.
// RELATIONSHIPS
//   Implements : IKeyGenerator (KeyKind.FastApiSecret)
//   Used by    : KeyGeneratorRegistry
//   See also   : SecureRandomSource, KeyFormatEncoder
// ============================================================================

using DeploymentVault.Core.KeyMaster.Abstractions;
using DeploymentVault.Core.KeyMaster.Domain;

namespace DeploymentVault.Platform.KeyMaster.Generation;

/// <summary>
/// Generates FastAPI secret keys (token_urlsafe equivalent).
/// </summary>
public sealed class FastApiSecretGenerator : IKeyGenerator
{
    /// <summary>Minimum bytes (256 bits).</summary>
    public const int MinBytes = 32;

    /// <inheritdoc/>
    public IReadOnlyCollection<KeyKind> Kinds { get; } = new[] { KeyKind.FastApiSecret };

    /// <inheritdoc/>
    public Task<GeneratedKey> GenerateAsync(GenerationSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        var profile = FrameworkProfile.For(spec.Kind);
        var byteCount = Math.Clamp(spec.Length, MinBytes, profile.MaxLength);
        var bytes = SecureRandomSource.Bytes(byteCount);
        var format = spec.Format == KeyFormat.Hex ? KeyFormat.Hex : KeyFormat.Base64Url;
        var value = KeyFormatEncoder.EncodeBytes(bytes, format);
        var entropy = EntropyEstimate.FromBytes(byteCount);

        return Task.FromResult(new GeneratedKey(
            value, null, spec.Kind, format, byteCount,
            entropy.Bits, DateTimeOffset.UtcNow, $"FastAPI · {byteCount} bytes"));
    }
}
