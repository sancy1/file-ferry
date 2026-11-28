// ============================================================================
// filepath: src/DeploymentVault.Platform/KeyMaster/Generation/KeyFormatEncoder.cs
// layer:    Platform / Generation
// module:   DeploymentVault.Platform.KeyMaster.Generation
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  Shared encoder: random bytes (or a character string) → the
//           requested KeyFormat. Raw for byte secrets means Base64URL (the
//           JWT/jose canonical form); Raw for character secrets means the
//           string itself. Pem/Json are handled by the key-pair generators.
// RELATIONSHIPS
//   Used by    : all KeyMaster generators
//   See also   : SecureRandomSource, KeyFormat
// ============================================================================

using DeploymentVault.Core.KeyMaster.Domain;

namespace DeploymentVault.Platform.KeyMaster.Generation;

/// <summary>
/// Encodes bytes or character strings into the requested <see cref="KeyFormat"/>.
/// </summary>
public static class KeyFormatEncoder
{
    /// <summary>Encodes random bytes. Raw maps to Base64URL.</summary>
    public static string EncodeBytes(byte[] bytes, KeyFormat format) => format switch
    {
        KeyFormat.Hex => Convert.ToHexString(bytes).ToLowerInvariant(),
        KeyFormat.Base64 => Convert.ToBase64String(bytes),
        KeyFormat.Base64Url or KeyFormat.Raw =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Format not supported for byte secrets."),
    };

    /// <summary>Encodes a character string. Raw returns it unchanged.</summary>
    public static string EncodeString(string value, KeyFormat format)
    {
        if (format == KeyFormat.Raw) return value;
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        return EncodeBytes(bytes, format);
    }
}
