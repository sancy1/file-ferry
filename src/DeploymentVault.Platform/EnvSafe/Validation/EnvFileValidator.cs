// ============================================================================
// filepath: src/DeploymentVault.Platform/EnvSafe/Validation/EnvFileValidator.cs
// layer:    Platform / Infrastructure
// module:   DeploymentVault.Platform.EnvSafe.Validation
// since:    Phase A (EnvSafe)
// purpose:  Detects whether .env content contains valid KEY=value pairs.
//           The validation gate runs before conversion; if no pairs are
//           found, the convert button is disabled.
//
// RELATIONSHIPS
//   Implements : IEnvFileValidator (DeploymentVault.Core.EnvSafe.Abstractions)
//   Depends on : System.Text.RegularExpressions
//   Used by    : DeploymentVaultServiceCollectionExtensions, EnvSafeViewModel
//   See also   : EnvFileConverter, SafetyScanner
//
// NOTES
//   - A valid KEY=value line: optional whitespace, optional "export ",
//     a key matching [A-Za-z_][A-Za-z0-9_]*, then "=". The value may be
//     empty. Comment lines (starting with #) and blank lines do not count.
//   - The regex is compiled once (static, non-backtracking).
// ============================================================================

using System.Text.RegularExpressions;
using DeploymentVault.Core.EnvSafe.Abstractions;

namespace DeploymentVault.Platform.EnvSafe.Validation;

/// <summary>
/// Detects KEY=value pairs in .env content.
/// </summary>
public sealed class EnvFileValidator : IEnvFileValidator
{
    private static readonly Regex KeyValuePattern = new(
        @"^\s*(?:export\s+)?[A-Za-z_][A-Za-z0-9_]*\s*=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <inheritdoc/>
    public bool HasKeyValuePairs(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return CountKeyValuePairs(content) > 0;
    }

    /// <inheritdoc/>
    public int CountKeyValuePairs(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var count = 0;
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;
            if (KeyValuePattern.IsMatch(line)) count++;
        }
        return count;
    }
}
