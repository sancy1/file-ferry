// ============================================================================
// filepath: src/DeploymentVault.Platform/KeyMaster/Generation/SecureRandomSource.cs
// layer:    Platform / Generation
// module:   DeploymentVault.Platform.KeyMaster.Generation
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  The single CSPRNG entry point for KeyMaster. Wraps
//           System.Security.Cryptography.RandomNumberGenerator. No fallback,
//           no System.Random, no Guid.NewGuid() as entropy. Charset sampling
//           uses rejection-free GetInt32 (uniform, unbiased).
// RELATIONSHIPS
//   Used by    : every KeyMaster generator
//   See also   : IKeyGenerator implementations
// ============================================================================

using System.Security.Cryptography;
using System.Text;

namespace DeploymentVault.Platform.KeyMaster.Generation;

/// <summary>
/// Cryptographically secure random source. All KeyMaster generators draw
/// entropy exclusively through this class.
/// </summary>
public static class SecureRandomSource
{
    /// <summary>Returns <paramref name="count"/> random bytes.</summary>
    public static byte[] Bytes(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        var buffer = new byte[count];
        RandomNumberGenerator.Fill(buffer);
        return buffer;
    }

    /// <summary>Returns a uniform random index in [0, maxExclusive).</summary>
    public static int Index(int maxExclusive) =>
        RandomNumberGenerator.GetInt32(maxExclusive);

    /// <summary>
    /// Builds a string of <paramref name="length"/> characters drawn uniformly
    /// from <paramref name="charset"/> using RandomNumberGenerator.GetInt32
    /// (unbiased — no modulo bias).
    /// </summary>
    public static string FromCharset(string charset, int length)
    {
        ArgumentException.ThrowIfNullOrEmpty(charset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = charset[Index(charset.Length)];
        }
        return new string(chars);
    }

    /// <summary>Lowercase hex of <paramref name="byteCount"/> random bytes.</summary>
    public static string Hex(int byteCount) =>
        Convert.ToHexString(Bytes(byteCount)).ToLowerInvariant();

    /// <summary>Standard Base64 of <paramref name="byteCount"/> random bytes.</summary>
    public static string Base64(int byteCount) =>
        Convert.ToBase64String(Bytes(byteCount));

    /// <summary>Base64URL (no padding) of <paramref name="byteCount"/> random bytes.</summary>
    public static string Base64Url(int byteCount) =>
        Convert.ToBase64String(Bytes(byteCount))
            .TrimEnd('=')
            .Replace('+', '-').Replace('/', '_');
}
