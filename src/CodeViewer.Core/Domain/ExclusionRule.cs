// filepath: src/CodeViewer.Core/Domain/ExclusionRule.cs
// ============================================================================
// PURPOSE:  One immutable exclusion rule: a name and a kind. Equality and
//           hashing are by (Name, Kind) with case-insensitive comparison on
//           Windows (OrdinalIgnoreCase), case-sensitive elsewhere, matching
//           the operating system's own file-name semantics. Names are
//           trimmed and validated in the constructor; empty, whitespace,
//           and invalid-character names are rejected.
// RELATION: Stored in GlobalExclusions. Persisted by
//           JsonGlobalExclusionStore. Checked by ProjectTreeEnumerator
//           during enumeration via GlobalExclusions.IsExcluded.
// ============================================================================

namespace CodeViewer.Core.Domain;

/// <summary>
/// One exclusion rule: an exact name and whether it targets a folder or a file.
/// </summary>
/// <param name="Name">
/// The exact name (not a path, not a pattern). Must be non-empty after trim
/// and must contain no directory separator characters.
/// </param>
/// <param name="Kind">Whether this rule excludes folders or files.</param>
/// <remarks>
/// <para>
/// Rule equality is case-insensitive on Windows (matching the filesystem)
/// and case-sensitive on Unix-like systems. This is implemented via
/// <see cref="NameComparer"/> below.
/// </para>
/// <para>
/// A folder rule and a file rule may have the same <see cref="Name"/>.
/// They are distinct rules because <see cref="Kind"/> differs.
/// </para>
/// </remarks>
public sealed record ExclusionRule
{
    /// <summary>
    /// The name comparer used for equality and hashing. OrdinalIgnoreCase on
    /// Windows, Ordinal on all other platforms.
    /// </summary>
    public static StringComparer NameComparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>The exact name (not a path, not a pattern).</summary>
    public string Name { get; }

    /// <summary>Whether this rule excludes folders or files.</summary>
    public ExclusionKind Kind { get; }

    /// <summary>
    /// Creates a rule. Throws when the name is empty, whitespace, or contains
    /// a path separator (rules target names, not paths).
    /// </summary>
    /// <param name="name">The name to exclude. Must not be null, empty, whitespace, or contain separators.</param>
    /// <param name="kind">The rule kind.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is null/empty/whitespace or contains
    /// <c>/</c>, <c>\</c>, or the platform's <see cref="Path.DirectorySeparatorChar"/>.
    /// </exception>
    public ExclusionRule(string name, ExclusionKind kind)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Rule name must not be empty or whitespace.", nameof(name));
        }

        var trimmed = name.Trim();
        if (trimmed.Contains('/' ) || trimmed.Contains('\\'))
        {
            throw new ArgumentException(
                "Rule name must be a single name, not a path (no '/' or '\\').", nameof(name));
        }

        Name = trimmed;
        Kind = kind;
    }

    /// <inheritdoc/>
    public bool Equals(ExclusionRule? other)
        => other is not null
        && Kind == other.Kind
        && NameComparer.Equals(Name, other.Name);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(Kind, NameComparer.GetHashCode(Name));

    /// <inheritdoc/>
    public override string ToString() => $"{Kind}:{Name}";
}