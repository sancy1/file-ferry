// filepath: src/CodeViewer.Core/Domain/GlobalExclusions.cs
// ============================================================================
// PURPOSE:  The global set of user exclusion rules. Immutable: every
//           Add/Remove returns a new instance, so callers can snapshot the
//           current rules and safely pass that snapshot to background
//           enumeration without worrying that the set will mutate under
//           them. Duplicate rules (same name+kind) are silently ignored by
//           Add, matching the "prevent accidental duplicates" UI contract.
// RELATION: Constructed by JsonGlobalExclusionStore on load and by
//           ManageExclusionsViewModel on Save & Apply. Consulted by
//           ProjectTreeEnumerator during directory enumeration via
//           IsExcluded. Held by GlobalExclusionsHolder for cross-tab
//           broadcast on change.
// ============================================================================

namespace CodeViewer.Core.Domain;

/// <summary>
/// Immutable set of user exclusion rules. Add and Remove return new
/// instances; the current instance is never mutated.
/// </summary>
public sealed class GlobalExclusions
{
    /// <summary>The empty rule set. Safe to share; never mutated.</summary>
    public static GlobalExclusions Empty { get; } = new(new HashSet<ExclusionRule>());

    private readonly HashSet<ExclusionRule> _rules;

    private GlobalExclusions(HashSet<ExclusionRule> rules) => _rules = rules;

    /// <summary>
    /// The current rules, in an unspecified order. Enumerating this set is
    /// safe; the set itself cannot be modified.
    /// </summary>
    public IReadOnlyCollection<ExclusionRule> Rules => _rules;

    /// <summary>True when there are no rules.</summary>
    public bool IsEmpty => _rules.Count == 0;

    /// <summary>
    /// Returns a new set that also contains <paramref name="rule"/>.
    /// If the rule is already present (name+kind), the current instance is
    /// returned unchanged. Never mutates this instance.
    /// </summary>
    public GlobalExclusions Add(ExclusionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (_rules.Contains(rule))
        {
            return this;
        }

        var next = new HashSet<ExclusionRule>(_rules) { rule };
        return new GlobalExclusions(next);
    }

    /// <summary>
    /// Returns a new set without <paramref name="rule"/>. If the rule is
    /// absent, the current instance is returned unchanged.
    /// </summary>
    public GlobalExclusions Remove(ExclusionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (!_rules.Contains(rule))
        {
            return this;
        }

        var next = new HashSet<ExclusionRule>(_rules);
        next.Remove(rule);
        return new GlobalExclusions(next);
    }

    /// <summary>True when the rule is present (name+kind).</summary>
    public bool Contains(ExclusionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return _rules.Contains(rule);
    }

    /// <summary>
    /// True when the name is excluded for the given kind. This is the method
    /// the directory enumerator consults. Comparison is case-insensitive on
    /// Windows, case-sensitive elsewhere, and exact by name (no partial
    /// matching, no patterns, no path components).
    /// </summary>
    /// <param name="name">The file or directory name (not the full path).</param>
    /// <param name="kind">Whether the caller is asking about a folder or a file.</param>
    public bool IsExcluded(string name, ExclusionKind kind)
    {
        if (string.IsNullOrEmpty(name) || _rules.Count == 0)
        {
            return false;
        }

        return _rules.Contains(new ExclusionRule(name, kind));
    }

    /// <summary>
    /// Builds a set from an enumerable of rules. Null or empty yields
    /// <see cref="Empty"/>.
    /// </summary>
    public static GlobalExclusions FromRules(IEnumerable<ExclusionRule>? rules)
    {
        if (rules is null)
        {
            return Empty;
        }

        var set = new HashSet<ExclusionRule>();
        foreach (var rule in rules)
        {
            if (rule is not null)
            {
                set.Add(rule);
            }
        }

        return set.Count == 0 ? Empty : new GlobalExclusions(set);
    }
}