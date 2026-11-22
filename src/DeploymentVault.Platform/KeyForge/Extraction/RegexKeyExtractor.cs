// ============================================================================
// filepath: src/DeploymentVault.Platform/KeyForge/Extraction/RegexKeyExtractor.cs
// layer:    Platform / Infrastructure
// module:   DeploymentVault.Platform.KeyForge.Extraction
// since:    Phase A (KeyForge)
// purpose:  Locates key-shaped spans in plain text using per-family regexes.
//           PEM blocks, JWTs, UUIDs, prefixed tokens, base64 blobs, and a
//           generic fallback. Non-overlapping matches are returned in order.
// RELATIONSHIPS
//   Implements : IKeyExtractor (DeploymentVault.Core.KeyForge.Abstractions)
//   Used by    : DeploymentVaultServiceCollectionExtensions, KeyForgeViewModel
//   See also   : ShapePreservingKeyForger
// NOTES
//   - Matches are collected from all family patterns, sorted by start, and
//     greedily accepted when they do not overlap an already-accepted span.
//   - The generic fallback scans remaining tokens of length >= 16 with at
//     least two character classes, excluding URLs, emails, ISO dates, file
//     paths, and plain words.
// ============================================================================

using System.Text.RegularExpressions;
using DeploymentVault.Core.KeyForge.Abstractions;
using DeploymentVault.Core.KeyForge.Domain;

namespace DeploymentVault.Platform.KeyForge.Extraction;

/// <summary>
/// Locates key-shaped spans using per-family regular expressions.
/// </summary>
public sealed partial class RegexKeyExtractor : IKeyExtractor
{
    private static readonly Regex PemPattern = new(
        @"-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z0-9 ]*PRIVATE KEY-----",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex JwtPattern = new(
        @"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UuidPattern = new(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PrefixedPattern = new(
        @"\b(?:[a-z]{2,5}[_-][A-Za-z0-9_-]{16,}|AIza[A-Za-z0-9_-]{16,})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Base64BlobPattern = new(
        @"\b[A-Za-z0-9+/]{40,}={0,2}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GenericTokenPattern = new(
        @"\b[A-Za-z0-9][A-Za-z0-9+/_-]{15,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IsoDatePattern = new(
        @"^\d{4}-\d{2}-\d{2}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <inheritdoc/>
    public IReadOnlyList<KeySpan> Extract(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) return Array.Empty<KeySpan>();

        // Process families in priority order so specific families claim their
        // spans before the generic fallback can swallow them. Each accepted
        // span marks its range occupied; later families skip overlaps.
        var occupied = new List<(int Start, int End)>();
        var result = new List<KeySpan>();

        CollectFamily(PemPattern, text, KeyFamily.PemBlock, occupied, result);
        CollectFamily(JwtPattern, text, KeyFamily.Jwt, occupied, result);
        CollectFamily(UuidPattern, text, KeyFamily.Uuid, occupied, result);
        CollectFamily(PrefixedPattern, text, KeyFamily.PrefixedToken, occupied, result);
        CollectFamily(Base64BlobPattern, text, KeyFamily.Base64Blob, occupied, result);
        CollectGeneric(text, occupied, result);

        result.Sort((a, b) => a.Start.CompareTo(b.Start));
        return result;
    }

    private static void CollectFamily(
        Regex pattern,
        string text,
        KeyFamily family,
        List<(int Start, int End)> occupied,
        List<KeySpan> result)
    {
        foreach (Match m in pattern.Matches(text))
        {
            if (!m.Success || m.Length == 0) continue;
            var end = m.Index + m.Length;
            if (Overlaps(occupied, m.Index, end)) continue;
            occupied.Add((m.Index, end));
            result.Add(new KeySpan(m.Index, m.Length, family, m.Value));
        }
    }

    private static void CollectGeneric(
        string text,
        List<(int Start, int End)> occupied,
        List<KeySpan> result)
    {
        foreach (Match m in GenericTokenPattern.Matches(text))
        {
            var value = m.Value;
            if (value.Length < 16) continue;
            if (!HasTwoCharClasses(value)) continue;
            if (IsExcluded(value)) continue;
            var end = m.Index + m.Length;
            if (Overlaps(occupied, m.Index, end)) continue;
            occupied.Add((m.Index, end));
            result.Add(new KeySpan(m.Index, m.Length, KeyFamily.Generic, value));
        }
    }

    private static bool HasTwoCharClasses(string value)
    {
        var hasUpper = false;
        var hasLower = false;
        var hasDigit = false;
        var hasSymbol = false;
        foreach (var c in value)
        {
            if (char.IsUpper(c)) hasUpper = true;
            else if (char.IsLower(c)) hasLower = true;
            else if (char.IsDigit(c)) hasDigit = true;
            else hasSymbol = true;
        }
        var classes = 0;
        if (hasUpper) classes++;
        if (hasLower) classes++;
        if (hasDigit) classes++;
        if (hasSymbol) classes++;
        return classes >= 2;
    }

    private static bool IsExcluded(string value)
    {
        if (value.Contains("://", StringComparison.Ordinal)) return true; // URL
        if (value.Contains('@')) return true; // email
        if (IsoDatePattern.IsMatch(value)) return true; // ISO date
        if (value.StartsWith('/')
            || value.StartsWith("./", StringComparison.Ordinal)
            || value.StartsWith("~/", StringComparison.Ordinal)) return true; // path
        if (value.Contains('/') && !value.Contains('+') && !value.EndsWith('=')) return true; // path-like
        return false;
    }

    private static bool Overlaps(List<(int Start, int End)> occupied, int start, int end)
    {
        foreach (var (s, e) in occupied)
        {
            if (start < e && end > s) return true;
        }
        return false;
    }
}
