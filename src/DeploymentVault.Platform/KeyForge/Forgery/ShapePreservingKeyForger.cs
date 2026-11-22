// ============================================================================
// filepath: src/DeploymentVault.Platform/KeyForge/Forgery/ShapePreservingKeyForger.cs
// layer:    Platform / Infrastructure
// module:   DeploymentVault.Platform.KeyForge.Forgery
// since:    Phase A (KeyForge)
// purpose:  Produces shape-identical fake keys. Each family preserves prefix,
//           character classes, length, separators, and structure; only the
//           characters differ. Deterministic mode seeds the RNG from the
//           original's SHA256 hash so the same input yields the same fake.
// RELATIONSHIPS
//   Implements : IKeyForger (DeploymentVault.Core.KeyForge.Abstractions)
//   Used by    : DeploymentVaultServiceCollectionExtensions, KeyForgeViewModel
//   See also   : RegexKeyExtractor
// NOTES
//   - Deterministic: Random seeded with the first 4 bytes of SHA256(original).
//   - Random: RandomNumberGenerator-based, unseeded.
//   - Character classes preserved: uppercase -> uppercase, lowercase ->
//     lowercase, digit -> digit, other -> unchanged (separators/symbols).
// ============================================================================

using System.Security.Cryptography;
using System.Text;
using DeploymentVault.Core.KeyForge.Abstractions;
using DeploymentVault.Core.KeyForge.Domain;

namespace DeploymentVault.Platform.KeyForge.Forgery;

/// <summary>
/// Produces shape-identical fake keys.
/// </summary>
public sealed class ShapePreservingKeyForger : IKeyForger
{
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Digit = "0123456789";
    private const string Hex = "0123456789abcdef";
    private const string Base64Url = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

    /// <inheritdoc/>
    public string Forge(string original, KeyFamily family, FakePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (original.Length == 0) return original;

        var rng = CreateRandom(original, policy);

        return family switch
        {
            KeyFamily.PemBlock => ForgePem(original, rng),
            KeyFamily.Jwt => ForgeJwt(original, rng),
            KeyFamily.Uuid => ForgeUuid(original, rng),
            KeyFamily.PrefixedToken => ForgePrefixed(original, rng),
            KeyFamily.Base64Blob => ForgeBase64(original, rng),
            _ => ForgeGeneric(original, rng),
        };
    }

    private static Random CreateRandom(string original, FakePolicy policy)
    {
        if (policy == FakePolicy.Random)
        {
            var bytes = new byte[4];
            RandomNumberGenerator.Fill(bytes);
            return new Random(BitConverter.ToInt32(bytes, 0));
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(original));
        var seed = BitConverter.ToInt32(hash, 0);
        return new Random(seed);
    }

    private static string ForgePrefixed(string original, Random rng)
    {
        // Keep the leading prefix + separator (lowercase letters + _ or -),
        // or the AIza prefix, then forge the rest preserving classes.
        var prefixLength = GetPrefixLength(original);
        var prefix = original[..prefixLength];
        var body = original[prefixLength..];
        return prefix + ReplacePreservingClass(body, rng);
    }

    private static int GetPrefixLength(string original)
    {
        if (original.StartsWith("AIza", StringComparison.Ordinal)) return 4;
        var i = 0;
        while (i < original.Length && char.IsLower(original[i])) i++;
        if (i < original.Length && (original[i] == '_' || original[i] == '-')) i++;
        return i;
    }

    private static string ForgeJwt(string original, Random rng)
    {
        var parts = original.Split('.');
        var sb = new StringBuilder(original.Length);
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0) sb.Append('.');
            sb.Append(ReplaceWithBase64Url(parts[i], rng));
        }
        return sb.ToString();
    }

    private static string ForgeUuid(string original, Random rng)
    {
        var sb = new StringBuilder(original.Length);
        foreach (var c in original)
        {
            if (c == '-') sb.Append('-');
            else sb.Append(Hex[rng.Next(Hex.Length)]);
        }
        return sb.ToString();
    }

    private static string ForgeBase64(string original, Random rng)
    {
        var sb = new StringBuilder(original.Length);
        foreach (var c in original)
        {
            if (c == '=') sb.Append('=');
            else sb.Append(Base64Url[rng.Next(Base64Url.Length)]);
        }
        return sb.ToString();
    }

    private static string ForgePem(string original, Random rng)
    {
        var sb = new StringBuilder(original.Length);
        var inBody = false;
        foreach (var line in original.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Contains("-----BEGIN", StringComparison.Ordinal))
            {
                inBody = true;
                sb.Append(line).Append('\n');
                continue;
            }
            if (trimmed.Contains("-----END", StringComparison.Ordinal))
            {
                inBody = false;
                sb.Append(line).Append('\n');
                continue;
            }
            if (inBody)
            {
                sb.Append(ReplaceWithBase64UrlPreserveNewline(line, rng)).Append('\n');
            }
            else
            {
                sb.Append(line).Append('\n');
            }
        }
        // Remove the trailing newline we added after the last line.
        if (sb.Length > 0 && sb[^1] == '\n') sb.Length--;
        return sb.ToString();
    }

    private static string ForgeGeneric(string original, Random rng)
    {
        return ReplacePreservingClass(original, rng);
    }

    private static string ReplacePreservingClass(string input, Random rng)
    {
        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (char.IsUpper(c)) sb.Append(Upper[rng.Next(Upper.Length)]);
            else if (char.IsLower(c)) sb.Append(Lower[rng.Next(Lower.Length)]);
            else if (char.IsDigit(c)) sb.Append(Digit[rng.Next(Digit.Length)]);
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static string ReplaceWithBase64Url(string input, Random rng)
    {
        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (char.IsUpper(c)) sb.Append(Upper[rng.Next(Upper.Length)]);
            else if (char.IsLower(c)) sb.Append(Lower[rng.Next(Lower.Length)]);
            else if (char.IsDigit(c)) sb.Append(Digit[rng.Next(Digit.Length)]);
            else if (c == '-' || c == '_') sb.Append(c);
            else sb.Append(Base64Url[rng.Next(Base64Url.Length)]);
        }
        return sb.ToString();
    }

    private static string ReplaceWithBase64UrlPreserveNewline(string line, Random rng)
    {
        var sb = new StringBuilder(line.Length);
        foreach (var c in line)
        {
            if (char.IsUpper(c)) sb.Append(Upper[rng.Next(Upper.Length)]);
            else if (char.IsLower(c)) sb.Append(Lower[rng.Next(Lower.Length)]);
            else if (char.IsDigit(c)) sb.Append(Digit[rng.Next(Digit.Length)]);
            else if (c == '+' || c == '/' || c == '=') sb.Append(c);
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
