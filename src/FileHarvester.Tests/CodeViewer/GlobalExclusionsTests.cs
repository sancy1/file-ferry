// filepath: src/FileHarvester.Tests/CodeViewer/GlobalExclusionsTests.cs
// ============================================================================
// PURPOSE:  Unit tests for the exclusion domain model: ExclusionRule
//           validation, name+kind equality, case sensitivity rules,
//           and GlobalExclusions CRUD (Add, Remove, Contains,
//           IsExcluded, FromRules, Empty). Proves the contract that
//           the enumeration integration will rely on.
// ============================================================================

using CodeViewer.Core.Domain;
using FluentAssertions;

namespace FileHarvester.Tests.CodeViewer;

public sealed class GlobalExclusionsTests
{
    [Fact]
    public void EmptyIsEmpty() => GlobalExclusions.Empty.IsEmpty.Should().BeTrue();

    [Fact]
    public void AddReturnsNewInstance()
    {
        var original = GlobalExclusions.Empty;
        var rule = new ExclusionRule("node_modules", ExclusionKind.Folder);
        var next = original.Add(rule);

        original.IsEmpty.Should().BeTrue("Add must not mutate the original");
        next.Rules.Should().Contain(rule);
    }

    [Fact]
    public void AddDuplicateIsIdempotent()
    {
        var rule = new ExclusionRule("node_modules", ExclusionKind.Folder);
        var once = GlobalExclusions.Empty.Add(rule);
        var twice = once.Add(rule);

        ReferenceEquals(once, twice).Should().BeTrue(
            "adding the same rule twice must return the same instance");
    }

    [Fact]
    public void RemoveReturnsNewInstance()
    {
        var rule = new ExclusionRule("node_modules", ExclusionKind.Folder);
        var with = GlobalExclusions.Empty.Add(rule);
        var without = with.Remove(rule);

        with.Rules.Should().Contain(rule);
        without.Rules.Should().BeEmpty();
    }

    [Fact]
    public void RemoveAbsentRuleIsNoop()
    {
        var rule = new ExclusionRule("x", ExclusionKind.File);
        var set = GlobalExclusions.Empty;
        var after = set.Remove(rule);
        ReferenceEquals(set, after).Should().BeTrue();
    }

    [Fact]
    public void FolderAndFileWithSameNameAreIndependent()
    {
        var folder = new ExclusionRule("build", ExclusionKind.Folder);
        var file = new ExclusionRule("build", ExclusionKind.File);
        var set = GlobalExclusions.Empty.Add(folder).Add(file);

        set.Rules.Should().HaveCount(2, "folder and file rules with the same name are distinct");
        set.IsExcluded("build", ExclusionKind.Folder).Should().BeTrue();
        set.IsExcluded("build", ExclusionKind.File).Should().BeTrue();
    }

    [Fact]
    public void IsExcludedMatchesExactNameOnly()
    {
        var set = GlobalExclusions.Empty.Add(new ExclusionRule("node_modules", ExclusionKind.Folder));
        set.IsExcluded("node_modules", ExclusionKind.Folder).Should().BeTrue();
        set.IsExcluded("node_modules2", ExclusionKind.Folder).Should().BeFalse("no partial matching");
        set.IsExcluded("my_node_modules", ExclusionKind.Folder).Should().BeFalse("no partial matching");
    }

    [Fact]
    public void IsExcludedIsCaseInsensitiveOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var set = GlobalExclusions.Empty.Add(new ExclusionRule("node_modules", ExclusionKind.Folder));
        set.IsExcluded("NODE_MODULES", ExclusionKind.Folder).Should().BeTrue();
        set.IsExcluded("Node_Modules", ExclusionKind.Folder).Should().BeTrue();
    }

    [Fact]
    public void IsExcludedForWrongKindReturnsFalse()
    {
        var set = GlobalExclusions.Empty.Add(new ExclusionRule("build", ExclusionKind.Folder));
        set.IsExcluded("build", ExclusionKind.File).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RuleRejectsEmptyName(string name)
    {
        var act = () => new ExclusionRule(name, ExclusionKind.Folder);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("path/to/file")]
    [InlineData("path\\to\\file")]
    public void RuleRejectsPathSeparators(string name)
    {
        var act = () => new ExclusionRule(name, ExclusionKind.Folder);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RuleTrimsWhitespace()
    {
        var rule = new ExclusionRule("  node_modules  ", ExclusionKind.Folder);
        rule.Name.Should().Be("node_modules");
    }

    [Fact]
    public void FromRulesBuildsSet()
    {
        var rules = new[]
        {
            new ExclusionRule("a", ExclusionKind.Folder),
            new ExclusionRule("b", ExclusionKind.File),
        };
        var set = GlobalExclusions.FromRules(rules);
        set.Rules.Should().HaveCount(2);
    }

    [Fact]
    public void FromRulesNullYieldsEmpty()
    {
        GlobalExclusions.FromRules(null).Should().BeSameAs(GlobalExclusions.Empty);
    }

    [Fact]
    public void FromRulesDeduplicates()
    {
        var rules = new[]
        {
            new ExclusionRule("dup", ExclusionKind.Folder),
            new ExclusionRule("dup", ExclusionKind.Folder),
        };
        GlobalExclusions.FromRules(rules).Rules.Should().HaveCount(1);
    }
}