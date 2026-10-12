// ============================================================================
// FILE:     src/FileHarvester.Tests/Documentation/ResilienceUsageClaimsTests.cs
// PURPOSE:  Documentation conformance tests for docs/resilience-usage.md.
//           Parses the pipe-separated policy table and asserts the
//           numbers match the code. Uses a stable structural marker
//           (the table), never prose.
// RELATION: Executable documentation tier.
// ============================================================================

using System.Globalization;
using System.Text.RegularExpressions;
using FileHarvester.Core.Composition;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Documentation;

public sealed class ResilienceUsageClaimsTests
{
    /// <summary>
    /// Locates the repository root from the test assembly's location.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
        {
            var docs = Path.Combine(dir, "docs", "resilience-usage.md");
            if (File.Exists(docs))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException(
            "Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// Reads a pipe-separated table row whose first cell matches the
    /// given name. Returns the cells after the name, or null when the
    /// row is not found.
    /// </summary>
    private static string[]? FindTableRow(string policyName)
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "docs", "resilience-usage.md");
        var lines = File.ReadAllLines(path);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('|'))
            {
                continue;
            }

            var cells = trimmed.Split((char)124)
                .Select(c => c.Trim())
                .ToArray();

            // cells[0] is empty (before the leading |), cells[1] is the
            // first content cell.
            if (cells.Length < 2)
            {
                continue;
            }

            if (string.Equals(cells[1], policyName, StringComparison.Ordinal))
            {
                return cells;
            }
        }

        return null;
    }

    [Fact]
    public void DocumentationListsFourCloudPolicies()
    {
        // The doc's policy table lists seven policies, four of which
        // are cloud-*. This test counts the cloud-* rows.
        var root = FindRepoRoot();
        var path = Path.Combine(root, "docs", "resilience-usage.md");
        var text = File.ReadAllText(path);

        var cloudRead = Regex.Count(text, @"\\|\\s*cloud-read\\s*\\|");
        var cloudWrite = Regex.Count(text, @"\\|\\s*cloud-write\\s*\\|");
        var cloudSearch = Regex.Count(text, @"\\|\\s*cloud-search\\s*\\|");
        var cloudAuth = Regex.Count(text, @"\\|\\s*cloud-auth\\s*\\|");

        cloudRead.Should().BeGreaterThan(0, "the table must document cloud-read");
        cloudWrite.Should().BeGreaterThan(0, "the table must document cloud-write");
        cloudSearch.Should().BeGreaterThan(0, "the table must document cloud-search");
        cloudAuth.Should().BeGreaterThan(0, "the table must document cloud-auth");
    }

    [Fact]
    public void CloudAuthOpenDurationIs300()
    {
        // The doc says cloud-auth opens the circuit for 300 seconds.
        var cells = FindTableRow(PolicyNames.CloudAuth);
        cells.Should().NotBeNull("the policy table must contain a cloud-auth row");

        // The Circuit open column is the seventh content cell (index 7
        // counting the leading empty cell). Locate it by scanning for
        // a cell equal to "300s".
        cells!.Should().Contain("300s",
            "the doc says cloud-auth opens for 300 seconds");
    }

    [Fact]
    public void CloudReadPermitLimitIs300()
    {
        // The doc says cloud-read permits 300 calls per minute.
        var cells = FindTableRow(PolicyNames.CloudRead);
        cells.Should().NotBeNull();
        cells!.Should().Contain("300/min",
            "the doc says cloud-read permits 300 per minute");
    }

    [Fact]
    public void CloudWritePermitLimitIs60()
    {
        // The doc says cloud-write permits 60 calls per minute.
        var cells = FindTableRow(PolicyNames.CloudWrite);
        cells.Should().NotBeNull();
        cells!.Should().Contain("60/min",
            "the doc says cloud-write permits 60 per minute");
    }
}
