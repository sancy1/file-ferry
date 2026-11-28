// ============================================================================
// filepath: src/DeploymentVault.Platform/KeyMaster/Generation/AesGcmKeyGenerator.cs
// layer:    Platform / Generation
// module:   DeploymentVault.Platform.KeyMaster.Generation
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  Go AES-GCM payload encryption key generator. AES-GCM accepts
//           exactly 16, 24, or 32 bytes — anything else is refused with
//           ArgumentException. Labeled "AES Key for GCM Payload Encryption".
// RELATIONSHIPS
//   Implements : IKeyGenerator (KeyKind.GoAesGcm)
//   Used by    : KeyGeneratorRegistry
//   See also   : SecureRandomSource, KeyFormatEncoder
// ============================================================================

using DeploymentVault.Core.KeyMaster.Abstractions;
using DeploymentVault.Core.KeyMaster.Domain;

namespace DeploymentVault.Platform.KeyMaster.Generation;

/// <summary>
/// Generates AES-GCM keys of exactly 16, 24, or 32 bytes.
/// </summary>
public sealed class AesGcmKeyGenerator : IKeyGenerator
{
    /// <inheritdoc/>
    public IReadOnlyCollection<KeyKind> Kinds { get; } = new[] { KeyKind.GoAesGcm };

    /// <inheritdoc/>
    public Task<GeneratedKey> GenerateAsync(GenerationSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        var byteCount = spec.Length;
        if (byteCount is not (16 or 24 or 32))
        {
            throw new ArgumentException(
                $"AES-GCM keys must be exactly 16, 24, or 32 bytes; got {byteCount}.",
                nameof(spec));
        }

        var bytes = SecureRandomSource.Bytes(byteCount);
        var format = spec.Format == KeyFormat.Hex ? KeyFormat.Hex : KeyFormat.Base64;
        var value = KeyFormatEncoder.EncodeBytes(bytes, format);
        var entropy = EntropyEstimate.FromBytes(byteCount);
        var bits = byteCount * 8;

        return Task.FromResult(new GeneratedKey(
            value, null, spec.Kind, format, byteCount,
            entropy.Bits, DateTimeOffset.UtcNow,
            $"AES-{bits} · {byteCount} bytes · GCM payload encryption"));
    }
}
