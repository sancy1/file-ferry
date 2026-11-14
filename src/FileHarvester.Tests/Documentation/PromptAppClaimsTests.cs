// ============================================================================
// FILE:     src/FileHarvester.Tests/Documentation/PromptAppClaimsTests.cs
// PURPOSE:  Documentation conformance tests for the Prompt App's
//           architecture.md and user-guide.md. Asserts every documented
//           claim against a real code artifact. Uses stable structural
//           markers: type existence, constant count, member values,
//           collection shape. Never parses prose.
// RELATION: Executable documentation tier. Mirrors ArchitectureClaimsTests
//           and PhaseMapClaimsTests.
// ============================================================================

using System;
using System.Linq;
using FileHarvester.App.ViewModels;
using FluentAssertions;
using Prompt.Core.Domain;
using Prompt.Platform.Composition;
using Prompt.Platform.Observability;
using Xunit;

namespace FileHarvester.Tests.Documentation;

public sealed class PromptAppClaimsTests
{
    private static readonly string[] ExpectedPolicyNames =
    {
        "prompt-store", "prompt-render", "prompt-export",
    };

    private static readonly string[] ExpectedEventNames =
    {
        "prompt_created", "prompt_updated", "prompt_deleted",
        "prompt_restored", "prompt_exported", "prompt_imported",
        "prompt_search_executed", "prompt_render_failed",
    };

    private static readonly string[] ExpectedShortcutKeys =
    {
        "Ctrl+N", "Ctrl+F", "Ctrl+E", "Delete", "Ctrl+S", "Ctrl+Z / Ctrl+Y",
    };

    [Fact]
    public void PromptPolicyNamesHasExactlyThreeConstants()
    {
        // architecture.md: "The three policy names are constants on
        // PromptPolicyNames." The values are asserted below.
        var fields = typeof(PromptPolicyNames).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly)
            .ToArray();
        fields.Should().HaveCount(3);
        fields.Select(f => (string)f.GetRawConstantValue()!)
            .Should().BeEquivalentTo(ExpectedPolicyNames);
    }

    [Fact]
    public void PromptEventNamesHasExactlyEightConstants()
    {
        // architecture.md: "The eight operation names are constants on
        // PromptEventNames."
        var fields = typeof(PromptEventNames).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly)
            .ToArray();
        fields.Should().HaveCount(8);
        fields.Select(f => (string)f.GetRawConstantValue()!)
            .Should().BeEquivalentTo(ExpectedEventNames);
    }

    [Fact]
    public void SevenDomainInterfacesExist()
    {
        // architecture.md: "The Domain exposes seven interfaces."
        typeof(IPromptStore).Should().NotBeNull();
        typeof(ICategoryStore).Should().NotBeNull();
        typeof(ISubCategoryStore).Should().NotBeNull();
        typeof(IPromptVersionStore).Should().NotBeNull();
        typeof(IPromptExporter).Should().NotBeNull();
        typeof(IPromptImporter).Should().NotBeNull();
        typeof(IPdfTextExtractor).Should().NotBeNull();
    }

    [Fact]
    public void BothAddPromptExtensionsExist()
    {
        // architecture.md: "Two extension methods on IServiceCollection."
        // They live on two different static classes:
        //   PromptServiceCollectionExtensions.AddPromptFeature
        //   PromptStoreServiceCollectionExtensions.AddPromptStores
        var featureMethods = typeof(PromptServiceCollectionExtensions).GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(m => m.Name == "AddPromptFeature")
            .ToArray();
        featureMethods.Should().HaveCount(1);

        var storeMethods = typeof(PromptStoreServiceCollectionExtensions).GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(m => m.Name == "AddPromptStores")
            .ToArray();
        storeMethods.Should().HaveCount(1);
    }

    [Fact]
    public void PromptEventEmitterAndNullImplementInterface()
    {
        // architecture.md: "PromptEventEmitter and NullPromptEventEmitter
        // both implement IPromptEventEmitter."
        typeof(IPromptEventEmitter).IsAssignableFrom(typeof(PromptEventEmitter)).Should().BeTrue();
        typeof(IPromptEventEmitter).IsAssignableFrom(typeof(NullPromptEventEmitter)).Should().BeTrue();
    }

    [Fact]
    public void ShortcutsDialogListsTheSixDocumentedShortcuts()
    {
        // user-guide.md: the six shortcuts. The dialog VM is the
        // authoritative list; the guide names the same six.
        var vm = new PromptShortcutsDialogViewModel();
        vm.Rows.Should().HaveCount(6);
        vm.Rows.Select(r => r.Keys).Should().BeEquivalentTo(ExpectedShortcutKeys);
    }

    [Fact]
    public void EveryShortcutRowHasNonEmptyDescription()
    {
        // user-guide.md: "each with a one-line description each."
        var vm = new PromptShortcutsDialogViewModel();
        vm.Rows.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Description));
    }

    [Fact]
    public void ShortcutDescriptionsMatchTheDocumentedActions()
    {
        // user-guide.md's table values, asserted against the dialog VM.
        var vm = new PromptShortcutsDialogViewModel();
        var map = vm.Rows.ToDictionary(r => r.Keys, r => r.Description);
        map["Ctrl+N"].Should().Be("New prompt");
        map["Ctrl+F"].Should().Be("Focus the search box");
        map["Ctrl+E"].Should().Be("Export the selected prompt");
        map["Delete"].Should().Be("Move the selected prompt to Trash");
        map["Ctrl+S"].Should().Be("Save the prompt being edited");
        map["Ctrl+Z / Ctrl+Y"].Should().Be("Undo / redo in the editor body");
    }

    [Fact]
    public void PhaseMapDocumentEndsWithTheSentinel()
    {
        // phase-map.md is the running closure ledger. It ends with the
        // "End of phase map." sentinel, the same convention as the
        // handover note.
        var root = FindRepoRoot();
        var path = System.IO.Path.Combine(root, "docs", "prompt-app", "phase-map.md");
        var lines = System.IO.File.ReadAllLines(path);
        lines.Should().NotBeEmpty();
        var last = lines.Last();
        last.Should().Contain("End of phase map");
    }

    [Fact]
    public void ArchitectureAndUserGuideDocsExist()
    {
        // architecture.md and user-guide.md are the two new documents
        // this claims test guards.
        var root = FindRepoRoot();
        System.IO.File.Exists(System.IO.Path.Combine(root, "docs", "prompt-app", "architecture.md"))
            .Should().BeTrue();
        System.IO.File.Exists(System.IO.Path.Combine(root, "docs", "prompt-app", "user-guide.md"))
            .Should().BeTrue();
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
        {
            var docs = System.IO.Path.Combine(dir, "docs", "prompt-app", "phase-map.md");
            if (System.IO.File.Exists(docs)) return dir;
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException(
            "Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}