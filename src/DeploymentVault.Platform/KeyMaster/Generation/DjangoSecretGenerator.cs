// ============================================================================
// filepath: src/DeploymentVault.Platform/KeyMaster/Generation/DjangoSecretGenerator.cs
// layer:    Platform / Generation
// module:   DeploymentVault.Platform.KeyMaster.Generation
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  Django SECRET_KEY generator. Enforces the 50-character minimum
//           (check --deploy) and offers three charset modes: Django default
//           (exact Django charset), Env-safe (Django minus '$' — the only
//           charset char that breaks .env/shell), and Raw (printable ASCII
//           minus whitespace). Refuses under 50 chars.
// RELATIONSHIPS
//   Implements : IKeyGenerator (KeyKind.DjangoSecret)
//   Used by    : KeyGeneratorRegistry
//   See also   : SecureRandomSource, KeyFormatEncoder
// ============================================================================

using DeploymentVault.Core.KeyMaster.Abstractions;
using DeploymentVault.Core.KeyMaster.Domain;

namespace DeploymentVault.Platform.KeyMaster.Generation;

/// <summary>
/// Generates Django SECRET_KEY values with the exact Django charset.
/// </summary>
public sealed class DjangoSecretGenerator : IKeyGenerator
{
    /// <summary>Django's exact get_random_secret_key charset.</summary>
    public const string DjangoCharset = "abcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*(-_=+)";

    /// <summary>Django charset minus '$' (breaks .env parsers and shells).</summary>
    public const string EnvSafeCharset = "abcdefghijklmnopqrstuvwxyz0123456789!@#%^&*(-_=+)";

    /// <summary>Printable ASCII 33-126 (raw mode).</summary>
    public const string RawCharset = "!\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

    /// <summary>Minimum characters Django's check --deploy requires.</summary>
    public const int MinChars = 50;

    /// <inheritdoc/>
    public IReadOnlyCollection<KeyKind> Kinds { get; } = new[] { KeyKind.DjangoSecret };

    /// <inheritdoc/>
    public Task<GeneratedKey> GenerateAsync(GenerationSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        var profile = FrameworkProfile.For(spec.Kind);
        var length = Math.Clamp(spec.Length, MinChars, profile.MaxLength);
        var charset = CharsetFor(spec.Algorithm);
        var value = SecureRandomSource.FromCharset(charset, length);
        var encoded = KeyFormatEncoder.EncodeString(value, spec.Format);
        var entropy = EntropyEstimate.FromPool(length, charset.Length);

        return Task.FromResult(new GeneratedKey(
            encoded, null, spec.Kind, spec.Format, length,
            entropy.Bits, DateTimeOffset.UtcNow,
            $"Django · {length} chars · {CharsetLabel(spec.Algorithm)}"));
    }

    /// <summary>Resolves the charset for a Django mode name.</summary>
    public static string CharsetFor(string mode) => mode switch
    {
        "EnvSafe" => EnvSafeCharset,
        "Raw" => RawCharset,
        _ => DjangoCharset,
    };

    private static string CharsetLabel(string mode) => mode switch
    {
        "EnvSafe" => "env-safe",
        "Raw" => "raw",
        _ => "django",
    };
}
