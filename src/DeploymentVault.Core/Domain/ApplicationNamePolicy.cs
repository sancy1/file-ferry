// ============================================================================
// filepath: src/DeploymentVault.Core/Domain/ApplicationNamePolicy.cs
// layer:    Domain
// module:   DeploymentVault.Core.Domain
// since:    Phase A (Deployment Vault registry)
// purpose:  Validates an application name. Rejects empty or whitespace
//           names, names containing characters that would break the export
//           folder structure in Phase B, and names that are reserved
//           device names on Windows.
//
// RELATIONSHIPS
//   Implements : n/a
//   Depends on : n/a (BCL only)
//   Used by    : DeploymentVaultViewModel before every create or rename,
//                SqliteDeploymentVaultStoreTests for the boundary cases,
//                ApplicationNamePolicyTests
//   See also   : Application
//
// NOTES
//   - The allowed character set is: Unicode letters, Unicode digits,
//     ASCII space, hyphen, underscore, dot. Everything else is rejected
//     with a specific reason.
//   - The Windows reserved names (CON, PRN, AUX, NUL, COM1..COM9,
//     LPT1..LPT9) are rejected case-insensitively. The check is applied
//     even on non-Windows hosts because the vault's export in Phase B
//     writes folder names, and a name that works on Linux but fails on
//     Windows would produce a broken export for a Windows consumer.
//   - The policy is a pure function. It does not touch the store, does
//     not check uniqueness. Uniqueness is the store's job (partial unique
//     index on LOWER(name) for non-deleted rows).
//   - The maximum length is 120 characters. This is a display constraint,
//     not a database constraint; the schema stores the name as TEXT with
//     no length cap. The 120-character limit is enforced by this policy.
// ============================================================================

using System.Text.RegularExpressions;

namespace DeploymentVault.Core.Domain;

/// <summary>
/// Validates an application name against the Deployment Vault rules.
/// </summary>
/// <remarks>
/// Pure function. Does not check uniqueness; that is the store's
/// responsibility. See the type remarks for the full rules.
/// </remarks>
public static class ApplicationNamePolicy
{
    /// <summary>The maximum allowed name length, in characters.</summary>
    public const int MaxLength = 120;

    /// <summary>
    /// The Windows reserved device names, case-insensitive. Rejected even
    /// on non-Windows hosts because the export writes folder names.
    /// </summary>
    private static readonly string[] ReservedWindowsNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// The allowed-character regex. Letters and digits are Unicode-aware;
    /// the literal punctuation is limited to space, hyphen, underscore,
    /// and dot.
    /// </summary>
    private static readonly Regex Allowed = new(
        @"^[\p{L}\p{Nd} _\-.]+$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    /// <summary>
    /// The characters rejected because they break a folder name on Windows.
    /// Cached as a SearchValues&lt;char&gt; so the IndexOfAny call is O(n)
    /// over the name rather than allocating the array on every call.
    /// </summary>
    private static readonly System.Buffers.SearchValues<char> InvalidChars =
        System.Buffers.SearchValues.Create(":*?\"<>|");

    /// <summary>
    /// Validates a candidate application name.
    /// </summary>
    /// <param name="name">
    /// The candidate. Null, empty, or whitespace is rejected with a specific
    /// reason. Not null-checked here; null produces a "required" reason.
    /// </param>
    /// <returns>
    /// A result. <see cref="NameValidationResult.IsValid"/> is true when
    /// the name is acceptable. When false, <see cref="NameValidationResult.Reason"/>
    /// names the specific rule that failed. Never null.
    /// </returns>
    public static NameValidationResult Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return NameValidationResult.Invalid("A name is required.");
        }

        if (name.Length > MaxLength)
        {
            return NameValidationResult.Invalid(
                $"The name must be {MaxLength} characters or fewer.");
        }

        if (name.Contains('/') || name.Contains('\\'))
        {
            return NameValidationResult.Invalid(
                "The name must not contain '/' or '\\'.");
        }

        if (name.AsSpan().IndexOfAny(InvalidChars) >= 0)
        {
            return NameValidationResult.Invalid(
                "The name must not contain ':', '*', '?', '\"', '<', '>', or '|'.");
        }

        if (!Allowed.IsMatch(name))
        {
            return NameValidationResult.Invalid(
                "The name may contain letters, digits, spaces, hyphens, underscores, and dots only.");
        }

        foreach (var reserved in ReservedWindowsNames)
        {
            if (string.Equals(name, reserved, StringComparison.OrdinalIgnoreCase))
            {
                return NameValidationResult.Invalid(
                    $"The name '{name}' is a reserved device name on Windows.");
            }
        }

        return NameValidationResult.Valid();
    }

    /// <summary>
    /// Compares two names for uniqueness purposes: case-insensitive,
    /// culture-invariant. Two names that compare equal would collide on
    /// the partial unique index in the schema.
    /// </summary>
    /// <param name="a">One name. Not null.</param>
    /// <param name="b">The other name. Not null.</param>
    /// <returns>True when the two names are the same for uniqueness purposes.</returns>
    public static bool AreEquivalent(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The result of <see cref="ApplicationNamePolicy.Validate"/>.
/// </summary>
/// <param name="IsValid">True when the name is acceptable.</param>
/// <param name="Reason">
/// When invalid, the specific rule that failed. When valid, empty string.
/// Never null.
/// </param>
public sealed record NameValidationResult(bool IsValid, string Reason)
{
    /// <summary>Creates a valid result. Reason is empty.</summary>
    public static NameValidationResult Valid() => new(true, string.Empty);

    /// <summary>Creates an invalid result with the given reason.</summary>
    /// <param name="reason">The specific rule that failed. Not null or whitespace.</param>
    public static NameValidationResult Invalid(string reason) => new(false, reason);
}
