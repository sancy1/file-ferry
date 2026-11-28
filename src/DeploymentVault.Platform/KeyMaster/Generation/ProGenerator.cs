// ============================================================================
// filepath: src/DeploymentVault.Platform/KeyMaster/Generation/ProGenerator.cs
// layer:    Platform / Generation
// module:   DeploymentVault.Platform.KeyMaster.Generation
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  Universal Pro-mode generator. Charset toggles (upper/lower/
//           digits/symbols), exclusion list, custom prefix/suffix, length
//           8-256 chars, formats Raw/Hex/Base64/Base64URL/JSON. Prefix and
//           suffix are appended AFTER generation — they add no entropy.
// RELATIONSHIPS
//   Implements : IKeyGenerator (KeyKind.ProKey)
//   Used by    : KeyGeneratorRegistry
//   See also   : SecureRandomSource.FromCharset, EntropyEstimate.FromPool
// ============================================================================

using System.Text.Json;
using DeploymentVault.Core.KeyMaster.Abstractions;
using DeploymentVault.Core.KeyMaster.Domain;

namespace DeploymentVault.Platform.KeyMaster.Generation;

/// <summary>
/// Universal generator with full charset control.
/// </summary>
public sealed class ProGenerator : IKeyGenerator
{
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Digits = "0123456789";
    private const string Symbols = "!@#$%^&*()";

    /// <inheritdoc/>
    public IReadOnlyCollection<KeyKind> Kinds { get; } = new[] { KeyKind.ProKey };

    /// <inheritdoc/>
    public Task<GeneratedKey> GenerateAsync(GenerationSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        var charset = BuildCharset(spec);
        if (charset.Length == 0)
        {
            throw new ArgumentException(
                "The character pool is empty. Enable at least one character set and check the exclusion list.",
                nameof(spec));
        }

        var length = Math.Clamp(spec.Length, 8, 256);
        var body = SecureRandomSource.FromCharset(charset, length);
        var value = spec.Prefix + body + spec.Suffix;

        var encoded = spec.Format switch
        {
            KeyFormat.Hex => KeyFormatEncoder.EncodeString(value, KeyFormat.Hex),
            KeyFormat.Base64 => KeyFormatEncoder.EncodeString(value, KeyFormat.Base64),
            KeyFormat.Base64Url => KeyFormatEncoder.EncodeString(value, KeyFormat.Base64Url),
            KeyFormat.Json => JsonSerializer.Serialize(new { key = value }),
            _ => value,
        };

        // Entropy counts only the random body — prefix/suffix add none.
        var entropy = EntropyEstimate.FromPool(length, charset.Length);
        return Task.FromResult(new GeneratedKey(
            encoded, null, spec.Kind, spec.Format, length,
            entropy.Bits, DateTimeOffset.UtcNow,
            $"Pro · {length} chars · pool {charset.Length}"));
    }

    /// <summary>
    /// Builds the effective charset from the toggles minus the exclusion list.
    /// Exposed for the VM's live entropy meter.
    /// </summary>
    public static string BuildCharset(GenerationSpec spec)
    {
        var sb = new System.Text.StringBuilder();
        if (spec.UseUpper) sb.Append(Upper);
        if (spec.UseLower) sb.Append(Lower);
        if (spec.UseDigits) sb.Append(Digits);
        if (spec.UseSymbols) sb.Append(Symbols);

        if (!string.IsNullOrEmpty(spec.Exclusions))
        {
            var excluded = new HashSet<char>(spec.Exclusions);
            var filtered = sb.ToString().Where(c => !excluded.Contains(c));
            return new string(filtered.Distinct().ToArray());
        }

        return sb.ToString();
    }
}
