// ============================================================================
// FILE:     src/FileHarvester.Tests/Documentation/PhaseMapClaimsTests.cs
// PURPOSE:  Documentation conformance test for docs/phase-map.md.
//           The doc's deferred-items table must name a scheduled
//           phase for every row. This test parses the pipe-separated
//           table and asserts the phase column is non-empty for every
//           row. Uses stable structural markers, never prose.
// RELATION: Executable documentation tier.
// ============================================================================

using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace FileHarvester.Tests.Documentation;

public sealed class PhaseMapClaimsTests
{
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
        {
            var docs = Path.Combine(dir, "docs", "phase-map.md");
            if (File.Exists(docs)) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException(
            "Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    [Fact]
    public void EveryDeferredItemHasScheduledPhase()
    {
        // The phase-map doc includes a "Deferred items" section with a
        // pipe-separated table: Item | Source | Scheduled phase.
        // Every row must have a non-empty third column.
        var root = FindRepoRoot();
        var path = Path.Combine(root, "docs", "phase-map.md");
        var lines = File.ReadAllLines(path);

        var inDeferredSection = false;
        var rowsChecked = 0;
        var rowsMissingPhase = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed == "## Deferred items")
            {
                inDeferredSection = true;
                continue;
            }

            if (inDeferredSection && trimmed.StartsWith('#') )
            {
                break;
            }

            if (!inDeferredSection) continue;
            if (!trimmed.StartsWith('|')) continue;

            var cells = trimmed.Split((char)124).Select(c => c.Trim()).ToArray();

            // Skip separator rows like |---|---|.
            if (cells.Length > 1 && cells[1].StartsWith('-')) continue;

            // Header row is | Item | Source | Scheduled phase |.
            if (cells.Length > 1 && cells[1] == "Item") continue;

            // Data rows have at least four cells: empty, item, source, phase.
            if (cells.Length < 4) continue;

            rowsChecked++;
            if (string.IsNullOrWhiteSpace(cells[3]))
            {
                rowsMissingPhase++;
            }
        }

        rowsChecked.Should().BeGreaterThan(0, "the doc must contain the deferred table");
        rowsMissingPhase.Should().Be(0,
            "every deferred item must name a scheduled phase");
    }
}
