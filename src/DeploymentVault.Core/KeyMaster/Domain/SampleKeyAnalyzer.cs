// ============================================================================
// filepath: src/DeploymentVault.Core/KeyMaster/Domain/SampleKeyAnalyzer.cs
// layer:    Core / Domain
// module:   DeploymentVault.Core.KeyMaster.Domain
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  Pure static analyzer for the Pro-mode "Match Sample" feature.
//           Detects length, charset composition, likely encoding (hex,
//           base64url, UUID), distinct-character count, and an entropy
//           estimate. No I/O, no randomness — fully deterministic.
// RELATIONSHIPS
//   Used by    : KeyMasterViewModel (Analyze Sample command)
//   See also   : SampleProfile, EntropyEstimate
// ============================================================================

using System.Text.RegularExpressions;

namespace DeploymentVault.Core.KeyMaster.Domain;

/// <summary>
/// Analyzes a pasted sample key and returns its statistical profile.
/// </summary>
public static class SampleKeyAnalyzer
{
    private static readonly Regex UuidPattern = new(
        @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HexPattern = new(
        @"^[0-9a-fA-F]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Base64UrlPattern = new(
        @"^[A-Za-z0-9_-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Analyzes <paramref name="sample"/> and returns its profile.
    /// Returns null for null/empty/whitespace input.
    /// </summary>
    public static SampleProfile? Analyze(string? sample)
    {
        if (string.IsNullOrWhiteSpace(sample)) return null;
        var s = sample.Trim();

        var hasUpper = s.Any(char.IsUpper);
        var hasLower = s.Any(char.IsLower);
        var hasDigits = s.Any(char.IsDigit);
        var hasSymbols = s.Any(c => !char.IsLetterOrDigit(c));
        var isUuid = UuidPattern.IsMatch(s);
        var isHex = !isUuid && HexPattern.IsMatch(s);
        var isBase64Url = !isUuid && !isHex && Base64UrlPattern.IsMatch(s);
        var distinct = s.Distinct().Count();

        // Entropy estimate: length * log2(observed pool). The observed pool is
        // approximated by the distinct characters actually present, floored at
        // the charset the composition implies.
        var impliedPool = distinct;
        if (isHex) impliedPool = Math.Max(impliedPool, 16);
        else if (isBase64Url) impliedPool = Math.Max(impliedPool, 64);
        var bits = s.Length * Math.Log2(Math.Max(2, impliedPool));

        return new SampleProfile(
            s.Length, hasUpper, hasLower, hasDigits, hasSymbols,
            isHex, isBase64Url, isUuid, bits, distinct);
    }
}
