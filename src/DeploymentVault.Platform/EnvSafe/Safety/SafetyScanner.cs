// ============================================================================
// filepath: src/DeploymentVault.Platform/EnvSafe/Safety/SafetyScanner.cs
// layer:    Platform / Infrastructure
// module:   DeploymentVault.Platform.EnvSafe.Safety
// since:    Phase A (EnvSafe)
// purpose:  Scans converted .env.example output for values that still look
//           sensitive. This is the app's second feature: a .env.example that
//           still contains a real secret is the failure mode EnvSafe exists
//           to prevent.
//
// RELATIONSHIPS
//   Implements : ISafetyScanner (DeploymentVault.Core.EnvSafe.Abstractions)
//   Depends on : System.Text.RegularExpressions, DeploymentVault.Core.EnvSafe.Domain
//   Used by    : DeploymentVaultServiceCollectionExtensions, EnvSafeViewModel
//   See also   : EnvFileConverter, EnvFileValidator
//
// NOTES
//   - Runs on the OUTPUT of the converter, not the input.
//   - The chosen placeholder style is used to recognize legitimate
//     placeholders so they are not flagged.
//   - Detection patterns (Phase A):
//       api-key-shaped        20+ char base64 or hex strings
//       jwt-shaped            three base64url segments separated by dots
//       url-with-credentials  scheme://user:password@host
//       pem-private-key       -----BEGIN ... PRIVATE KEY-----
//       long-random-secret    24+ chars mixed-case + digits + symbols
//       non-placeholder-value any KEY=value value that does not match the
//                             chosen placeholder style
// ============================================================================

using System.Text.RegularExpressions;
using DeploymentVault.Core.EnvSafe.Abstractions;
using DeploymentVault.Core.EnvSafe.Domain;

namespace DeploymentVault.Platform.EnvSafe.Safety;

/// <summary>
/// Scans converted .env.example output for remaining sensitive values.
/// </summary>
public sealed class SafetyScanner : ISafetyScanner
{
    private static readonly Regex JwtPattern = new(
        @"^eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UrlWithCredentialsPattern = new(
        @"^[a-zA-Z][a-zA-Z0-9+.-]*://[^:/\s]+:[^@\s]+@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HexSecretPattern = new(
        @"^[0-9a-fA-F]{20,}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Base64SecretPattern = new(
        @"^(?=.*[0-9A-Z])[A-Za-z0-9_+/=-]{20,}={0,2}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LongRandomSecretPattern = new(
        @"^(?=.*[a-z])(?=.*[A-Z])(?=.*[0-9])(?=.*[^A-Za-z0-9])[A-Za-z0-9!@#$%^&*()_+\-=\[\]{};':""\\|,.<>\/?]{24,}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex KeyValueLinePattern = new(
        @"^\s*(?:export\s+)?[A-Za-z_][A-Za-z0-9_]*\s*=\s*(.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <inheritdoc/>
    public IReadOnlyList<SafetyWarning> Scan(string output, PlaceholderStyle style)
    {
        ArgumentNullException.ThrowIfNull(output);
        var warnings = new List<SafetyWarning>();
        var lines = output.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var lineNumber = i + 1;
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            if (trimmed.Contains("-----BEGIN", StringComparison.Ordinal)
                && trimmed.Contains("PRIVATE KEY", StringComparison.Ordinal))
            {
                warnings.Add(new SafetyWarning(lineNumber, "pem-private-key", "PEM private key block detected", trimmed));
                continue;
            }

            var kv = KeyValueLinePattern.Match(line);
            if (kv.Success)
            {
                var rawValue = kv.Groups[1].Value.Trim();
                var value = Unquote(rawValue);
                if (value.Length == 0) continue;
                if (IsPlaceholder(value, style)) continue;

                if (JwtPattern.IsMatch(value))
                {
                    warnings.Add(new SafetyWarning(lineNumber, "jwt-shaped", "JWT-shaped value detected", trimmed));
                    continue;
                }

                if (UrlWithCredentialsPattern.IsMatch(value))
                {
                    warnings.Add(new SafetyWarning(lineNumber, "url-with-credentials", "URL with embedded credentials detected", trimmed));
                    continue;
                }

                if (HexSecretPattern.IsMatch(value) || Base64SecretPattern.IsMatch(value))
                {
                    warnings.Add(new SafetyWarning(lineNumber, "api-key-shaped", "API-key-shaped value (20+ base64/hex chars)", trimmed));
                    continue;
                }

                if (LongRandomSecretPattern.IsMatch(value))
                {
                    warnings.Add(new SafetyWarning(lineNumber, "long-random-secret", "Long random-looking secret (24+ mixed chars)", trimmed));
                    continue;
                }

                warnings.Add(new SafetyWarning(lineNumber, "non-placeholder-value", "Value does not match the chosen placeholder style", trimmed));
                continue;
            }

            // Non-KEY=value lines: still catch standalone JWTs and credentialed URLs.
            if (JwtPattern.IsMatch(trimmed))
            {
                warnings.Add(new SafetyWarning(lineNumber, "jwt-shaped", "JWT-shaped value detected", trimmed));
                continue;
            }

            if (UrlWithCredentialsPattern.IsMatch(trimmed))
            {
                warnings.Add(new SafetyWarning(lineNumber, "url-with-credentials", "URL with embedded credentials detected", trimmed));
            }
        }

        return warnings;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }
        return value;
    }

    private static bool IsPlaceholder(string value, PlaceholderStyle style)
    {
        return style switch
        {
            PlaceholderStyle.YourXHere => Regex.IsMatch(value, @"^your_[a-z0-9_]+_here$", RegexOptions.CultureInvariant),
            PlaceholderStyle.UpperXHere => Regex.IsMatch(value, @"^YOUR_[A-Z0-9_]+_HERE$", RegexOptions.CultureInvariant),
            PlaceholderStyle.AngleXHere => Regex.IsMatch(value, @"^<your_[a-z0-9_]+_here>$", RegexOptions.CultureInvariant),
            PlaceholderStyle.ChangeMe => value == "CHANGE_ME",
            _ => false,
        };
    }
}
