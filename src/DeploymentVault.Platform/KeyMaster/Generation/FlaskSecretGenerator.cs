// ============================================================================
// filepath: src/DeploymentVault.Platform/KeyMaster/Generation/FlaskSecretGenerator.cs
// layer:    Platform / Generation
// module:   DeploymentVault.Platform.KeyMaster.Generation
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  Flask SECRET_KEY generator. Two use cases: Session/Cookie signing
//           (hex canonical) and General API secret (URL-safe Base64
//           canonical). Minimum 24 bytes, default 32.
// RELATIONSHIPS
//   Implements : IKeyGenerator (KeyKind.FlaskSecret)
//   Used by    : KeyGeneratorRegistry
//   See also   : SecureRandomSource, KeyFormatEncoder
// ============================================================================

using DeploymentVault.Core.KeyMaster.Abstractions;
using DeploymentVault.Core.KeyMaster.Domain;

namespace DeploymentVault.Platform.KeyMaster.Generation;

/// <summary>
/// Generates Flask SECRET_KEY values for session or API use.
/// </summary>
public sealed class FlaskSecretGenerator : IKeyGenerator
{
    /// <summary>Minimum bytes.</summary>
    public const int MinBytes = 24;

    /// <inheritdoc/>
    public IReadOnlyCollection<KeyKind> Kinds { get; } = new[] { KeyKind.FlaskSecret };

    /// <inheritdoc/>
    public Task<GeneratedKey> GenerateAsync(GenerationSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        var profile = FrameworkProfile.For(spec.Kind);
        var byteCount = Math.Clamp(spec.Length, MinBytes, profile.MaxLength);
        var bytes = SecureRandomSource.Bytes(byteCount);

        // Algorithm selects the use case: "Session" → hex default,
        // "Api" → urlsafe default. An explicit spec.Format overrides.
        var isSession = !string.Equals(spec.Algorithm, "Api", StringComparison.OrdinalIgnoreCase);
        var format = spec.Format is KeyFormat.Hex or KeyFormat.Base64 or KeyFormat.Base64Url
            ? spec.Format
            : isSession ? KeyFormat.Hex : KeyFormat.Base64Url;

        var value = KeyFormatEncoder.EncodeBytes(bytes, format);
        var entropy = EntropyEstimate.FromBytes(byteCount);
        var label = isSession ? "Flask session" : "Flask API";

        return Task.FromResult(new GeneratedKey(
            value, null, spec.Kind, format, byteCount,
            entropy.Bits, DateTimeOffset.UtcNow, $"{label} · {byteCount} bytes"));
    }
}
